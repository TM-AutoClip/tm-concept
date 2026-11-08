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
    /// Creates the tables when the file has none yet (a new database, created by EF Core from the mapping) in one transaction; a file with
    /// tables is used as it is. Every way a database file comes in goes through here: first start, unlock, and a restored backup. A file of
    /// another layout is not recognised or upgraded (no backward compatibility): it fails where the mapping does not fit. An SQLite build
    /// without secure delete is refused.
    /// </summary>
    internal static async Task EnsureSchemaAsync(ConceptDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var secureDelete = await db.SecureDelete.Select(s => s.Value).FirstAsync(cancellationToken).ConfigureAwait(false);
        if (secureDelete != ConceptSchema.SecureDeleteOn)
        {
            throw new InvalidOperationException("This SQLite build does not overwrite deleted data (secure_delete is off); the database is not used.");
        }

        if (await db.SchemaObjects.AnyAsync(o => o.Type == ConceptSchema.TableType, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
