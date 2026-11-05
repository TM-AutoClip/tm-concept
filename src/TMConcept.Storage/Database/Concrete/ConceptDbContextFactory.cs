using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Paths;
using TMConcept.Storage.Keys;

namespace TMConcept.Storage.Database;

/// <summary>
/// Short-lived contexts on the encrypted file. The key goes in the connection string's Password (Microsoft.Data.Sqlite sends it as
/// <c>PRAGMA key</c>; SQLite3 Multiple Ciphers encrypts with its default ChaCha20-Poly1305). Pooling is off so a closed database releases
/// its file (restore replaces it). Foreign keys are on (cascades) and so is secure delete.
/// </summary>
public sealed class ConceptDbContextFactory(IAppPaths paths, DatabaseKey key) : IDbContextFactory<ConceptDbContext>
{
    /// <inheritdoc />
    public ConceptDbContext CreateDbContext() => new(Options(ConnectionString(paths.DatabaseFile, key.Passphrase())));

    /// <summary>Connection string of an encrypted file.</summary>
    public static string ConnectionString(string file, string passphrase) => new SqliteConnectionStringBuilder
    {
        DataSource = file,
        Password = passphrase,
        Pooling = false,
        ForeignKeys = true,
        Mode = SqliteOpenMode.ReadWriteCreate,
    }.ToString();

    /// <summary>Context options for a connection string.</summary>
    public static DbContextOptions<ConceptDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<ConceptDbContext>().UseSqlite(connectionString).AddInterceptors(SecureDeleteInterceptor.Instance).Options;
}
