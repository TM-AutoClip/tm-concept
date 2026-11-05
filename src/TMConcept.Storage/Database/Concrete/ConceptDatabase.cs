using Microsoft.Data.Sqlite;
using TMConcept.Core.Paths;
using TMConcept.Storage.Keys;

namespace TMConcept.Storage.Database;

/// <summary>
/// The encrypted database (SQLite3 Multiple Ciphers; the whole file is encrypted, table names included), opened for each piece of work and
/// closed after it. The key goes in the connection string's Password (Microsoft.Data.Sqlite sends it as <c>PRAGMA key</c>; SQLite3
/// Multiple Ciphers encrypts with its default ChaCha20-Poly1305). Pooling is off so a closed database releases its file (restore replaces
/// it). Foreign keys are on (cascades: deleting a profile removes its concepts, queue, image records, embeddings and video records;
/// deleting a concept removes its embedding and queue entry and leaves its videos with an empty link) and so is secure delete. Since 0.1.3
/// plain SQL replaces EF Core (which cannot be trimmed); the file format is unchanged (<see cref="ConceptSchema"/>, <see cref="Sql"/>).
/// </summary>
public sealed class ConceptDatabase(IAppPaths paths, DatabaseKey key)
{
    /// <summary>Opens the database of this data folder.</summary>
    public Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken) =>
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

    /// <summary>Opens a connection and turns on <c>PRAGMA secure_delete</c> (deleted rows are overwritten with zeros).</summary>
    public static async Task<SqliteConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await Sql.ExecuteAsync(connection, "PRAGMA secure_delete = ON;", cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Creates the tables when the file has none yet (a new database); an existing schema is left as it is.</summary>
    public static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var tables = await Sql.ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'", cancellationToken).ConfigureAwait(false);
        if (tables > 0)
        {
            return;
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Sql.ExecuteAsync(connection, ConceptSchema.Create, cancellationToken, transaction).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
