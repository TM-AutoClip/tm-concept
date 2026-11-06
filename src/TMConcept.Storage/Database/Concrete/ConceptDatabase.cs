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

    /// <summary>
    /// Creates the tables when the file has none yet (a new database, version <see cref="ConceptSchema.Version"/>); an older file is
    /// brought to the current schema one version after the other, in one transaction (all or nothing); a current file is left as it is.
    /// Every way a database file comes in goes through here: first start, unlock, and a restored backup (a backup of an older version is
    /// upgraded before it is used). A file of a newer version than this app is refused (it would be damaged by older code).
    /// </summary>
    public static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var version = await VersionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (version == ConceptSchema.Version)
        {
            return;
        }

        if (version > ConceptSchema.Version)
        {
            throw new InvalidOperationException(
                $"The database was made by a newer version of TM Concept (schema {version}; this version knows {ConceptSchema.Version}). Update the app.");
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (version == 0)
        {
            await Sql.ExecuteAsync(connection, ConceptSchema.Create, cancellationToken, transaction).ConfigureAwait(false);
        }
        else
        {
            if (version < 2)
            {
                await Sql.ExecuteAsync(connection, ConceptSchema.UpgradeTo2, cancellationToken, transaction).ConfigureAwait(false);
            }

            if (version < 3)
            {
                await Sql.ExecuteAsync(connection, ConceptSchema.UpgradeTo3, cancellationToken, transaction).ConfigureAwait(false);
            }
        }

        await Sql.ExecuteAsync(connection, "PRAGMA user_version = " + ConceptSchema.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";",
            cancellationToken, transaction).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The schema version of a file: 0 = no table yet; otherwise <c>PRAGMA user_version</c>, and for a file that has none written (0.1.0–0.1.5)
    /// 2 when it has <see cref="ConceptSchema.Version2Table"/>, else 1.
    /// </summary>
    public static async Task<int> VersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var tables = await Sql.ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'", cancellationToken).ConfigureAwait(false);
        if (tables is null or 0)
        {
            return 0;
        }

        var written = (int)(await Sql.ScalarAsync(connection, "PRAGMA user_version;", cancellationToken).ConfigureAwait(false) ?? 0);
        if (written > 0)
        {
            return written;
        }

        var has2 = await Sql.ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name", cancellationToken, null,
            ("$name", ConceptSchema.Version2Table)).ConfigureAwait(false);
        return has2 > 0 ? 2 : 1;
    }
}
