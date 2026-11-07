using Microsoft.Extensions.Logging.Abstractions;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Keys;

namespace TMConcept.Tests.Storage;

/// <summary>A vault with a master-password protector (works on every OS) in a temp folder.</summary>
internal sealed class StorageHarness : IDisposable
{
    public StorageHarness(string password = "correct horse battery")
    {
        Passwords = new FixedPasswords(password);
        Protector = new PasswordKeyProtector(Passwords, KdfParameters.Fast);
        Vault = new Vault(Paths, Key, [Protector], NullLogger<Vault>.Instance);
        Database = new ConceptDatabase(Paths, Key);
    }

    public TempPaths Paths { get; } = new();

    public DatabaseKey Key { get; } = new();

    public FixedPasswords Passwords { get; }

    public PasswordKeyProtector Protector { get; }

    public Vault Vault { get; }

    public ConceptDatabase Database { get; }

    /// <summary>This harness's database file.</summary>
    public string ConnectionString => ConceptDatabase.ConnectionString(Paths.DatabaseFile, Key.Passphrase());

    /// <summary>Runs <paramref name="query"/> on a database file (through EF Core, as the app reads it).</summary>
    public static async Task<T> QueryAsync<T>(string connectionString, Func<ConceptDbContext, Task<T>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = await ConceptDatabase.OpenAsync(connectionString, CancellationToken.None);
        return await query(db);
    }

    /// <summary>Runs <paramref name="query"/> on this harness's database.</summary>
    public Task<T> QueryAsync<T>(Func<ConceptDbContext, Task<T>> query) => QueryAsync(ConnectionString, query);

    public static async Task<StorageHarness> CreatedAsync()
    {
        var harness = new StorageHarness();
        await harness.Vault.CreateAsync(harness.Protector, CancellationToken.None);
        return harness;
    }

    public void Dispose() => Paths.Dispose();
}
