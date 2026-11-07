using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TMConcept.Core.Paths;
using TMConcept.Storage.Keys;

namespace TMConcept.Storage.Database;

/// <summary>
/// The encrypted database (SQLite3 Multiple Ciphers; the whole file is encrypted, table names included) through EF Core
/// (<see cref="ConceptDbContext"/>), opened for each piece of work and closed after it. The key goes in the connection string's Password
/// (Microsoft.Data.Sqlite sends it as <c>PRAGMA key</c>; SQLite3 Multiple Ciphers encrypts with its default ChaCha20-Poly1305). Pooling is
/// off so a closed database releases its file (restore replaces it). Foreign keys are on (cascades: deleting a profile removes its concepts,
/// queue, image records, embeddings and video records; deleting a concept removes its embedding and queue entry and leaves its videos with
/// an empty link). Secure delete (deleted content overwritten with zeros) is how SQLite3 Multiple Ciphers is built; it is checked whenever
/// a file comes in. 0.1.3–0.1.6 wrote the SQL by hand; since 0.1.7 EF Core writes every statement again (no SQL is written by hand). The
/// file format is unchanged.
/// </summary>
public sealed class ConceptDatabase(IAppPaths paths, DatabaseKey key)
{
    // The schema history, oldest first (each step brings its From version one further).
    private static readonly SchemaUpgradeBase[] Upgrades = [new SchemaUpgradeTo2(), new SchemaUpgradeTo3()];

    /// <summary>Opens the database of this data folder.</summary>
    internal Task<ConceptDbContext> OpenAsync(CancellationToken cancellationToken) =>
        OpenAsync(ConnectionString(paths.DatabaseFile, key.Passphrase()), cancellationToken);

    /// <summary>Connection string of an encrypted file.</summary>
    public static string ConnectionString(string file, string passphrase) => new SqliteConnectionStringBuilder
    {
        DataSource = file,
        Password = passphrase,
        Pooling = false,
        ForeignKeys = true,
        Mode = SqliteOpenMode.ReadWriteCreate,
    }.ToString();

    /// <summary>
    /// A context on <paramref name="connectionString"/> with its connection open (one connection for the whole piece of work: opening an
    /// encrypted file derives its key, so EF Core must not open and close it around every query). Queries do not track.
    /// </summary>
    internal static async Task<ConceptDbContext> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var db = new ConceptDbContext(new DbContextOptionsBuilder<ConceptDbContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            return db;
        }
        catch
        {
            await db.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Creates the tables when the file has none yet (a new database, version <see cref="ConceptSchema.Version"/>, created by EF Core as
    /// EnsureCreated did in 0.1.0–0.1.2); an older file is brought to the current schema one version after the other, in one transaction
    /// (all or nothing), on a second connection without foreign key enforcement (see <see cref="SchemaUpgradeBase"/>); a current file is
    /// left as it is. Every way a database file comes in goes through here: first start, unlock, and a restored backup (a backup of an older
    /// version is upgraded before it is used). A file of a newer version than this app is refused (it would be damaged by older code), and
    /// so is an SQLite build without secure delete.
    /// </summary>
    internal static async Task EnsureSchemaAsync(ConceptDbContext db, CancellationToken cancellationToken)
    {
        var (secureDelete, version) = await StateAsync(db, cancellationToken).ConfigureAwait(false);
        if (secureDelete != ConceptSchema.SecureDeleteOn)
        {
            throw new InvalidOperationException("This SQLite build does not overwrite deleted data (secure_delete is off); the database is not used.");
        }

        if (version == ConceptSchema.Version)
        {
            return;
        }

        if (version > ConceptSchema.Version)
        {
            throw new InvalidOperationException(
                $"The database was made by a newer version of TM Concept (schema {version}; this version knows {ConceptSchema.Version}). Update the app.");
        }

        var withoutForeignKeys = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()) { ForeignKeys = false }.ToString();
        await using var schema = await OpenAsync(withoutForeignKeys, cancellationToken).ConfigureAwait(false);
        await using var transaction = await schema.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (version == 0)
        {
            await schema.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            foreach (var upgrade in Upgrades.Where(u => u.From >= version))
            {
                await upgrade.ApplyAsync(schema, await TablesAsync(schema, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
        }

        var value = ConceptSchema.Version.ToString(CultureInfo.InvariantCulture);
        await schema.UpsertAsync(new SettingRow { Key = ConceptSchema.VersionSetting, Value = value }, s => s.Key == ConceptSchema.VersionSetting, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The schema version of a file: 0 = no table yet; otherwise the settings row <see cref="ConceptSchema.VersionSetting"/> (0.1.7 on) or
    /// <c>PRAGMA user_version</c> (0.1.6), whichever is higher; for a file with neither (0.1.0–0.1.5): 2 when it has
    /// <see cref="ConceptSchema.ProfileStepModels"/> and still <see cref="ConceptSchema.GeneralStepModels"/>, 1 without the first.
    /// </summary>
    internal static async Task<int> VersionAsync(ConceptDbContext db, CancellationToken cancellationToken) =>
        (await StateAsync(db, cancellationToken).ConfigureAwait(false)).Version;

    /// <summary>
    /// <c>pragma_secure_delete</c> and the schema version (<see cref="VersionAsync"/>). Few queries on purpose: this runs at every unlock, and
    /// each new query costs its first translation there (both pragmas are read in one).
    /// </summary>
    private static async Task<(long SecureDelete, int Version)> StateAsync(ConceptDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var pragmas = await db.SecureDelete.Select(s => new { SecureDelete = s.Value, UserVersion = db.UserVersion.Select(v => v.Value).First() })
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        var tables = await TablesAsync(db, cancellationToken).ConfigureAwait(false);
        if (tables.Count == 0)
        {
            return (pragmas.SecureDelete, 0);
        }

        var written = 0;
        if (tables.Contains(ConceptSchema.Settings)
            && await db.Settings.Where(s => s.Key == ConceptSchema.VersionSetting).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) is { } text)
        {
            written = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
                ? number
                : throw new InvalidOperationException($"The schema version in the database is damaged ('{text}').");
        }

        var marked = (int)Math.Max(written, pragmas.UserVersion);
        if (marked > 0)
        {
            return (pragmas.SecureDelete, marked);
        }

        return (pragmas.SecureDelete, !tables.Contains(ConceptSchema.ProfileStepModels) ? 1
            : tables.Contains(ConceptSchema.GeneralStepModels) ? 2
            : ConceptSchema.Version);
    }

    private static async Task<IReadOnlySet<string>> TablesAsync(ConceptDbContext db, CancellationToken cancellationToken) =>
        (await db.SchemaObjects.Where(o => o.Type == ConceptSchema.TableType).Select(o => o.Name).ToListAsync(cancellationToken).ConfigureAwait(false))
        .ToHashSet(StringComparer.Ordinal);
}
