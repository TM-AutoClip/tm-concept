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
/// a file comes in. EF Core writes every statement (no SQL is written by hand). Only files of this schema version are opened
/// (<see cref="ConceptSchema"/>).
/// </summary>
public sealed class ConceptDatabase(IAppPaths paths, DatabaseKey key)
{
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
    /// Creates the tables when the file has none yet (a new database, version <see cref="ConceptSchema.Version"/>, created by EF Core) in one
    /// transaction; a current file is left as it is. Every way a database file comes in goes through here: first start, unlock, and a
    /// restored backup. A file of any other version — older (0.1.0–0.1.6 wrote no version row) or newer — is refused with
    /// <see cref="DatabaseVersionException"/> and left unchanged (older files are not upgraded any more), and so is an SQLite build without
    /// secure delete.
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
            throw new DatabaseVersionException(
                $"The database was made by a newer version of TM Concept (schema {version}; this version uses {ConceptSchema.Version}). Update the app.")
            {
                Found = version,
            };
        }

        if (version != 0)
        {
            throw new DatabaseVersionException(
                $"The database was made by an older version of TM Concept (schema {(version < 0 ? "unknown" : version)}; this version uses {ConceptSchema.Version}). "
                + "Older files are not upgraded.")
            {
                Found = Math.Max(version, 0),
            };
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken).ConfigureAwait(false);
        db.Settings.Add(new SettingRow { Key = ConceptSchema.VersionSetting, Value = ConceptSchema.Version.ToString(CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The schema version of a file: 0 = no table yet (a new file); the settings row <see cref="ConceptSchema.VersionSetting"/> when it is
    /// there; −1 for a file with tables but no readable version (0.1.0–0.1.6 wrote none).
    /// </summary>
    internal static async Task<int> VersionAsync(ConceptDbContext db, CancellationToken cancellationToken) =>
        (await StateAsync(db, cancellationToken).ConfigureAwait(false)).Version;

    /// <summary>
    /// <c>pragma_secure_delete</c> and the schema version (<see cref="VersionAsync"/>). Few queries on purpose: this runs at every unlock, and
    /// each new query costs its first translation there.
    /// </summary>
    private static async Task<(long SecureDelete, int Version)> StateAsync(ConceptDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var secureDelete = await db.SecureDelete.Select(s => s.Value).FirstAsync(cancellationToken).ConfigureAwait(false);
        var tables = await db.SchemaObjects.Where(o => o.Type == ConceptSchema.TableType).Select(o => o.Name).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (tables.Count == 0)
        {
            return (secureDelete, 0);
        }

        if (!tables.Contains(ConceptSchema.Settings, StringComparer.Ordinal))
        {
            return (secureDelete, -1);
        }

        var text = await db.Settings.Where(s => s.Key == ConceptSchema.VersionSetting).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return (secureDelete, text is not null && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : -1);
    }
}
