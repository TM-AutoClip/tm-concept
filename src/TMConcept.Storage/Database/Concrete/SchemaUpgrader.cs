using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace TMConcept.Storage.Database;

/// <summary>
/// Brings a database file to this app's schema through the chain of schema steps (<see cref="ISchemaSteps"/>; developer decision,
/// 16 Nov 2026, replacing "no backward compatibility"): each app version that changes the tables adds ONE step that knows only the schema
/// before it, and a file from several versions ago goes through every step in order. The file records its steps, so it says itself which
/// version of the schema it has.
/// <list type="bullet">
/// <item>A new, empty file gets every step at once (nothing to lose, no copy).</item>
/// <item>An existing file with steps to go: first a copy of it is kept (<see cref="ConceptSchema.UpgradeBackupFolder"/>, the newest
/// <see cref="ConceptSchema.UpgradeBackupsKept"/>), then the steps run on a working copy next to it, and only when every step went through
/// does the working copy replace the file. A failed step leaves the file exactly as it was (<see cref="DatabaseVersionProblem.UpgradeFailed"/>).</item>
/// <item>A file with tables but no record (made by 0.1.16 or earlier) and a file that records a step this app does not know (made by a
/// newer version) are not changed and not opened (<see cref="DatabaseVersionException"/>).</item>
/// </list>
/// Every database file goes through here: the first start, every unlock, and a restored backup (an older backup is upgraded the same way).
/// Every statement comes from EF Core: no SQL is written by hand.
/// </summary>
internal sealed partial class SchemaUpgrader(ISchemaSteps steps, ILogger logger, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>The name of a step (<c>20261116120000_Version0117</c> → <c>Version0117</c>).</summary>
    public static string Name(string step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var match = StepId().Match(step);
        return match.Success ? match.Groups[1].Value : step;
    }

    /// <summary>
    /// Brings <paramref name="file"/> (encrypted with <paramref name="passphrase"/>) to the newest step. <paramref name="backupFolder"/>: where
    /// the copy made before an upgrade goes (null = no copy, for a file that is itself a temporary copy).
    /// </summary>
    public async Task<SchemaUpgradeResult> UpgradeFileAsync(string file, string passphrase, string? backupFolder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        SchemaState state;
        await using (var db = await ConceptDatabase.OpenAsync(ConceptDatabase.ConnectionString(file, passphrase), cancellationToken).ConfigureAwait(false))
        {
            state = await ReadAsync(db, cancellationToken).ConfigureAwait(false);
            if (state.Pending.Count == 0 || !state.HasTables)
            {
                // Current, or a new file: nothing in it can be lost, the steps run on it directly.
                return await ApplyAsync(db, state, null, cancellationToken).ConfigureAwait(false);
            }
        }

        SqliteConnection.ClearAllPools();
        var backup = backupFolder is null ? null : Backup(file, backupFolder, state.Last);
        var work = file + ConceptSchema.UpgradingSuffix;
        File.Copy(file, work, overwrite: true);
        try
        {
            await using (var db = await ConceptDatabase.OpenAsync(ConceptDatabase.ConnectionString(work, passphrase), cancellationToken).ConfigureAwait(false))
            {
                await ApplyAsync(db, state, backup, cancellationToken).ConfigureAwait(false);
            }

            SqliteConnection.ClearAllPools();
            File.Move(work, file, overwrite: true);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            Delete(work);
            throw;
        }

        logger.LogInformation("Database upgraded from {From} to {To}", Shown(state.Last), Name(state.Pending[^1]));
        return new SchemaUpgradeResult(state.Last, state.Pending, backup);
    }

    /// <summary>
    /// Brings the open <paramref name="db"/> to the newest step in place: for a new file and for a file that is itself a temporary copy (a
    /// backup being restored). The same checks as <see cref="UpgradeFileAsync"/>.
    /// </summary>
    public async Task<SchemaUpgradeResult> UpgradeInPlaceAsync(ConceptDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var state = await ReadAsync(db, cancellationToken).ConfigureAwait(false);
        return await ApplyAsync(db, state, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What the open file records, checked: secure delete on; a file with tables records its steps; it records no step this app does not
    /// know.
    /// </summary>
    private async Task<SchemaState> ReadAsync(ConceptDbContext db, CancellationToken cancellationToken)
    {
        var secureDelete = await db.SecureDelete.Select(s => s.Value).FirstAsync(cancellationToken).ConfigureAwait(false);
        if (secureDelete != ConceptSchema.SecureDeleteOn)
        {
            throw new InvalidOperationException("This SQLite build does not overwrite deleted data (secure_delete is off); the database is not used.");
        }

        var hasTables = await db.SchemaObjects.AnyAsync(o => o.Type == ConceptSchema.TableType && !o.Name.StartsWith(ConceptSchema.SqliteOwnPrefix)
            && !o.Name.StartsWith(ConceptSchema.StepRecordPrefix), cancellationToken).ConfigureAwait(false);
        var known = steps.Known(db);
        var applied = await steps.AppliedAsync(db, cancellationToken).ConfigureAwait(false);
        if (hasTables && applied.Count == 0)
        {
            logger.LogError("The database has tables but no record of its schema steps (made by TM Concept 0.1.16 or earlier); it is not changed");
            throw new DatabaseVersionException(
                "This database was made by TM Concept 0.1.16 or earlier, before database upgrades were recorded; this version cannot open it.")
            {
                Problem = DatabaseVersionProblem.Unrecorded,
            };
        }

        if (applied.FirstOrDefault(a => !known.Contains(a, StringComparer.Ordinal)) is { } newer)
        {
            logger.LogError("The database records the schema step {Step}, which this version does not know (made by a newer TM Concept); it is not changed",
                Name(newer));
            throw new DatabaseVersionException($"This database was made or upgraded by a newer TM Concept (schema step {Name(newer)}); update the app to open it.")
            {
                Problem = DatabaseVersionProblem.Newer,
                Step = Name(newer),
            };
        }

        var pending = known.Where(k => !applied.Contains(k, StringComparer.Ordinal)).ToList();
        return new SchemaState(hasTables, applied.Count > 0 ? applied[^1] : string.Empty, pending);
    }

    /// <summary>Runs the pending steps in order on the open file; the first failure stops with the step's name.</summary>
    private async Task<SchemaUpgradeResult> ApplyAsync(ConceptDbContext db, SchemaState state, string? backup, CancellationToken cancellationToken)
    {
        foreach (var step in state.Pending)
        {
            var watch = Stopwatch.StartNew();
            logger.LogInformation("Database schema step {Step} starts (the file is at {From})", Name(step), Shown(state.Last));
            try
            {
                await steps.ApplyAsync(db, step, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not DatabaseVersionException)
            {
                logger.LogError(ex, "Database schema step {Step} failed; the database stays as it was", Name(step));
                throw new DatabaseVersionException($"The database could not be upgraded (step {Name(step)}): {ex.Message}", ex)
                {
                    Problem = DatabaseVersionProblem.UpgradeFailed,
                    Step = Name(step),
                };
            }

            logger.LogInformation("Database schema step {Step} done in {Milliseconds} ms", Name(step), watch.ElapsedMilliseconds);
        }

        return new SchemaUpgradeResult(state.Last, state.Pending, backup);
    }

    /// <summary>
    /// A copy of the file as it is before the upgrade (still encrypted with its own key), named after the time and the step it is at; only the
    /// newest <see cref="ConceptSchema.UpgradeBackupsKept"/> stay.
    /// </summary>
    private string Backup(string file, string folder, string last)
    {
        Directory.CreateDirectory(folder);
        var stamp = _clock.GetUtcNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var target = Path.Combine(folder,
            ConceptSchema.UpgradeBackupPrefix + stamp + "-" + (last.Length > 0 ? Name(last) : "none") + ConceptSchema.UpgradeBackupExtension);
        File.Copy(file, target, overwrite: true);
        logger.LogInformation("A copy of the database was kept before the upgrade ({File})", Path.GetFileName(target));
        var old = Directory.EnumerateFiles(folder, ConceptSchema.UpgradeBackupPrefix + "*" + ConceptSchema.UpgradeBackupExtension)
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Skip(ConceptSchema.UpgradeBackupsKept)
            .ToList();
        foreach (var extra in old)
        {
            try
            {
                File.Delete(extra);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "An old copy of the database could not be deleted");
            }
        }

        return target;
    }

    private void Delete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The working copy of a failed upgrade could not be deleted; it is replaced at the next upgrade");
        }
    }

    private static string Shown(string step) => step.Length > 0 ? Name(step) : "(new file)";

    [GeneratedRegex(@"^\d{14}_(.+)$")]
    private static partial Regex StepId();

    /// <summary>A file's schema: whether it has tables, its newest applied step (empty = none) and the steps it lacks, oldest first.</summary>
    private sealed record SchemaState(bool HasTables, string Last, IReadOnlyList<string> Pending);
}
