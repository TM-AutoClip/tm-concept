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

    /// <summary>Rows in a table of a database file (plain SQL, as a reader of the file would see them).</summary>
    public static async Task<long> CountAsync(string connectionString, string table, string where = "")
    {
        await using var db = await ConceptDatabase.OpenAsync(connectionString, CancellationToken.None);
        await using var command = db.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"" + (where.Length > 0 ? " WHERE " + where : string.Empty);
        return (long)(await command.ExecuteScalarAsync(CancellationToken.None))!;
    }

    /// <summary>Rows in a table of this harness's database.</summary>
    public Task<long> CountAsync(string table, string where = "") => CountAsync(ConceptDatabase.ConnectionString(Paths.DatabaseFile, Key.Passphrase()), table, where);

    public static async Task<StorageHarness> CreatedAsync()
    {
        var harness = new StorageHarness();
        await harness.Vault.CreateAsync(harness.Protector, CancellationToken.None);
        return harness;
    }

    public void Dispose() => Paths.Dispose();
}
