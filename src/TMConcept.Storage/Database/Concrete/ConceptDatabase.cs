using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
/// a file comes in. EF Core writes every statement (no SQL is written by hand).
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
    /// Brings this data folder's database file to this version's schema (<see cref="SchemaUpgrader"/>): a new file gets its tables, an older
    /// one goes through every schema step it lacks after a copy of it is kept under the backup folder, a file that cannot be upgraded throws
    /// <see cref="DatabaseVersionException"/> and stays as it was. Called before the file is used (unlock, first start).
    /// </summary>
    public Task<SchemaUpgradeResult> UpgradeAsync(ILogger? logger, CancellationToken cancellationToken) =>
        new SchemaUpgrader(EfSchemaSteps.Instance, logger ?? NullLogger.Instance).UpgradeFileAsync(paths.DatabaseFile, key.Passphrase(),
            Path.Combine(paths.BackupDirectory, ConceptSchema.UpgradeBackupFolder), cancellationToken);

    /// <summary>
    /// Brings the open <paramref name="db"/> to this version's schema in place: a new file gets its tables; a file that is itself a temporary
    /// copy (a backup being restored, an older one too) goes through the schema steps it lacks. The same refusals as
    /// <see cref="UpgradeAsync"/>; an SQLite build without secure delete is refused.
    /// </summary>
    internal static Task<SchemaUpgradeResult> EnsureSchemaAsync(ConceptDbContext db, CancellationToken cancellationToken, ILogger? logger = null) =>
        new SchemaUpgrader(EfSchemaSteps.Instance, logger ?? NullLogger.Instance).UpgradeInPlaceAsync(db, cancellationToken);
}
