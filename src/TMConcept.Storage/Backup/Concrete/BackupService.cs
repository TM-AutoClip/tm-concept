using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Settings;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Keys;

namespace TMConcept.Storage.Backup;

/// <summary>
/// Backups as one file (<c>.tmcbak</c>): <see cref="SealedBox"/> with the magic word <c>TMCBAK01</c> around
/// [key length (1) | database key (32) | encrypted database copy]. The copy is made with SQLite's online backup into a file encrypted with
/// the same key, so no plain data is written to disk at any moment; API keys are deleted from the copy before it is sealed (decision: keys
/// never go into a backup). Only the database is backed up: videos and sample images stay in the user's folder.
/// The backup password is required at the first start; the derived key is kept in the database, so daily backups need no password.
/// A forgotten backup password cannot be recovered. A restore is possible only while there is no database yet (first start).
/// </summary>
public sealed class BackupService(IAppPaths paths, DatabaseKey key, Vault vault, IDbContextFactory<ConceptDbContext> factory, ISettingsStore settings,
    KdfParameters kdf, TimeProvider clock, ILogger<BackupService> logger)
{
    private const string KeySetting = "backup-key";
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("TMCBAK01");

    /// <summary>Whether a backup password was set.</summary>
    public async Task<bool> HasPasswordAsync(CancellationToken cancellationToken) => await LoadKeyAsync(cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>Sets (or changes) the backup password; later backups open with the new password, older files with the old one.</summary>
    public async Task SetPasswordAsync(string password, CancellationToken cancellationToken)
    {
        if (!BackupPasswordPolicy.IsAcceptable(password))
        {
            throw new ArgumentException($"The backup password must be at least {BackupPasswordPolicy.MinimumLength} characters.", nameof(password));
        }

        var derived = SealedBox.Derive(password, kdf);
        var record = new BackupKeyRecord
        {
            Salt = Convert.ToBase64String(derived.Salt),
            MemoryKiB = derived.Kdf.MemoryKiB,
            Iterations = derived.Kdf.Iterations,
            Parallelism = derived.Kdf.Parallelism,
            Key = Convert.ToBase64String(derived.Key),
        };
        CryptographicOperations.ZeroMemory(derived.Key);
        var value = JsonSerializer.Serialize(record, StorageJsonContext.Default.BackupKeyRecord);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == KeySetting, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            db.Settings.Add(new SettingRow { Key = KeySetting, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Backup password set");
    }

    /// <summary>Writes a backup of the open database to <paramref name="target"/>.</summary>
    public async Task CreateAsync(string target, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        var backupKey = await LoadKeyAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No backup password was set.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var copy = TemporaryFile();
        var dek = key.Copy();
        try
        {
            var passphrase = key.Passphrase();
            using (var source = new SqliteConnection(ConceptDbContextFactory.ConnectionString(paths.DatabaseFile, passphrase)))
            using (var destination = new SqliteConnection(ConceptDbContextFactory.ConnectionString(copy, passphrase)))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
                using var command = destination.CreateCommand();
                command.CommandText = "PRAGMA secure_delete = ON; DELETE FROM api_keys; VACUUM;";
                command.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();
            var database = await File.ReadAllBytesAsync(copy, cancellationToken).ConfigureAwait(false);
            var plain = new byte[1 + dek.Length + database.Length];
            plain[0] = (byte)dek.Length;
            dek.CopyTo(plain, 1);
            database.CopyTo(plain, 1 + dek.Length);
            var sealedData = SealedBox.Seal(Magic, backupKey, plain);
            CryptographicOperations.ZeroMemory(plain);
            var temporary = target + ".tmp";
            await File.WriteAllBytesAsync(temporary, sealedData, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: true);
            logger.LogInformation("Backup written ({Kilobytes} KB)", sealedData.Length / 1024);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
            CryptographicOperations.ZeroMemory(backupKey.Key);
            Delete(copy);
        }
    }

    /// <summary>Writes a backup into the backup folder now and keeps the last <see cref="BackupRules.Keep"/>.</summary>
    public async Task<string> CreateInFolderAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var target = Path.Combine(paths.BackupDirectory,
            BackupRules.Prefix + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + BackupRules.Extension);
        await CreateAsync(target, cancellationToken).ConfigureAwait(false);
        Prune();
        var current = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
        current.LastBackupUtc = now;
        await settings.SaveAsync(current, cancellationToken).ConfigureAwait(false);
        return target;
    }

    /// <summary>
    /// The daily backup: written when the last one is not from today (local date). Returns the file, or <c>null</c> when none was due or no
    /// password is set (the reason is logged).
    /// </summary>
    public async Task<string?> RunDailyAsync(CancellationToken cancellationToken)
    {
        if (!await HasPasswordAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("Daily backup skipped: no backup password is set");
            return null;
        }

        var current = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
        var today = clock.GetLocalNow().Date;
        if (current.LastBackupUtc is { } last && TimeZoneInfo.ConvertTime(last, clock.LocalTimeZone).Date >= today)
        {
            return null;
        }

        return await CreateInFolderAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The backups in the backup folder, newest first.</summary>
    public IReadOnlyList<BackupFile> List()
    {
        if (!Directory.Exists(paths.BackupDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(paths.BackupDirectory, "*" + BackupRules.Extension)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupFile(f.FullName, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero), f.Length))
            .ToList();
    }

    /// <summary>
    /// Restores a backup into this computer's data folder. Allowed only before a database exists (first start). The key is sealed again with
    /// <paramref name="protector"/> (this computer's keyring or a master password). <see cref="SealedBoxException"/> for a wrong password or
    /// a damaged file — the two are deliberately not told apart.
    /// </summary>
    public async Task<RestoreReport> RestoreAsync(string source, string password, IKeyProtector protector, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(protector);
        if (vault.State != VaultState.NotCreated)
        {
            throw new InvalidOperationException("A backup can be restored only at the first start, before any data exists.");
        }

        var sealedData = await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false);
        var plain = SealedBox.Open(Magic, password, sealedData);
        var copy = TemporaryFile();
        try
        {
            if (plain.Length < 33 || plain[0] != 32)
            {
                throw new SealedBoxException("The backup content is damaged.");
            }

            var dek = plain.AsSpan(1, 32).ToArray();
            await File.WriteAllBytesAsync(copy, plain.AsMemory(33).ToArray(), cancellationToken).ConfigureAwait(false);
            await VerifyAsync(copy, dek, cancellationToken).ConfigureAwait(false);
            if (File.Exists(paths.DatabaseFile))
            {
                // A database file without a key file cannot be opened by anyone; it is moved aside, never deleted.
                File.Move(paths.DatabaseFile, paths.DatabaseFile + ".orphan-" + clock.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            }

            await vault.InstallAsync(copy, dek, protector, cancellationToken).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(dek);
            return await ReportAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            Delete(copy);
        }
    }

    private async Task<DerivedKey?> LoadKeyAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == KeySetting, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var record = JsonSerializer.Deserialize(row.Value, StorageJsonContext.Default.BackupKeyRecord)
            ?? throw new InvalidDataException("The stored backup key is damaged.");
        return new DerivedKey(Convert.FromBase64String(record.Salt), new KdfParameters(record.MemoryKiB, record.Iterations, record.Parallelism),
            Convert.FromBase64String(record.Key));
    }

    private void Prune()
    {
        foreach (var old in List().Where(f => System.IO.Path.GetFileName(f.Path).StartsWith(BackupRules.Prefix, StringComparison.Ordinal)).Skip(BackupRules.Keep))
        {
            try
            {
                File.Delete(old.Path);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Old backup could not be deleted");
            }
        }
    }

    private static async Task VerifyAsync(string file, byte[] dek, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = new ConceptDbContext(ConceptDbContextFactory.Options(ConceptDbContextFactory.ConnectionString(file, Convert.ToHexString(dek))));
            await db.Settings.CountAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            throw new SealedBoxException("The database inside the backup does not open; the backup is damaged.", ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private async Task<RestoreReport> ReportAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var report = new RestoreReport(
            await db.Profiles.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Concepts.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Videos.CountAsync(cancellationToken).ConfigureAwait(false));
        logger.LogInformation("Restore finished: {Profiles} profiles, {Concepts} concepts, {Videos} videos", report.Profiles, report.Concepts, report.Videos);
        return report;
    }

    private string TemporaryFile() => System.IO.Path.Combine(paths.DataDirectory, "backup-" + Guid.NewGuid().ToString("N") + ".tmp");

    private void Delete(string file)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(file);
        }
        catch (IOException ex)
        {
            // The temporary copy is encrypted; it is only left behind when Windows still holds the file. Logged, not hidden.
            logger.LogWarning(ex, "Temporary backup file could not be deleted");
        }
    }
}
