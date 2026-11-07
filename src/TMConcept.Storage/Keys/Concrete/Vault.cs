using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Keys;

/// <summary>
/// Creates, opens and re-protects the encrypted database. A new 32-byte random key (DEK) opens the database; the DEK is stored only sealed
/// by a protector (DPAPI on Windows, Secret Service on Linux, or a master password). The user does not type anything when an operating
/// system protector is used.
/// </summary>
public sealed class Vault(IAppPaths paths, DatabaseKey key, IEnumerable<IKeyProtector> protectors, ILogger<Vault> logger)
{
    private readonly List<IKeyProtector> _protectors = protectors.ToList();

    /// <summary>Current state.</summary>
    public VaultState State => key.IsLoaded ? VaultState.Open : File.Exists(paths.KeyFile) ? VaultState.Locked : VaultState.NotCreated;

    /// <summary>The automatic protector of this computer (DPAPI / Secret Service), or <c>null</c> when none works.</summary>
    public IKeyProtector? AutomaticProtector => _protectors.FirstOrDefault(p => p.Automatic && p.IsAvailable);

    /// <summary>The master password protector.</summary>
    public IKeyProtector PasswordProtector => _protectors.First(p => p.Method == KeyMethods.Password);

    /// <summary>Method used by the current key file (<c>null</c> before creation).</summary>
    public string? Method => KeyFile.Read(paths.KeyFile)?.Method;

    /// <summary>First start: a new key, sealed with <paramref name="protector"/>, and an empty database with the schema.</summary>
    public async Task CreateAsync(IKeyProtector protector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(protector);
        if (State != VaultState.NotCreated)
        {
            throw new InvalidOperationException("The database already exists.");
        }

        var dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var envelope = await protector.ProtectAsync(dek, cancellationToken).ConfigureAwait(false);
            key.Load(dek);
            if (File.Exists(paths.DatabaseFile))
            {
                // A database without a key file cannot be opened by anyone; it is moved aside, never deleted.
                File.Move(paths.DatabaseFile, paths.DatabaseFile + ".orphan-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture));
            }

            await using (var db = await new ConceptDatabase(paths, key).OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                await ConceptDatabase.EnsureSchemaAsync(db, cancellationToken).ConfigureAwait(false);
            }

            KeyFile.Write(paths.KeyFile, envelope);
            logger.LogInformation("Encrypted database created (key protection: {Method})", protector.Method);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    /// <summary>Opens the database with the key file's protector; an exception with the reason when it cannot.</summary>
    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        var envelope = KeyFile.Read(paths.KeyFile) ?? throw new InvalidOperationException("There is no key file; the database was never created here.");
        var protector = _protectors.FirstOrDefault(p => p.Method == envelope.Method)
            ?? throw new InvalidOperationException($"The key file uses '{envelope.Method}', which this app does not know.");
        if (!protector.IsAvailable)
        {
            throw new InvalidOperationException($"The key protection '{envelope.Method}' is not available on this computer now.");
        }

        var dek = await protector.UnprotectAsync(envelope, cancellationToken).ConfigureAwait(false);
        try
        {
            key.Load(dek);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }

        try
        {
            await using var db = await new ConceptDatabase(paths, key).OpenAsync(cancellationToken).ConfigureAwait(false);
            // Reading the schema state (tables, settings row, pragmas) is the check that the key opens the file.
            await ConceptDatabase.EnsureSchemaAsync(db, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            key.Clear();
            throw;
        }

        logger.LogInformation("Encrypted database opened (key protection: {Method})", envelope.Method);
    }

    /// <summary>Seals the open key with another protector (for example "lock with a master password") and forgets the old seal.</summary>
    public async Task ChangeProtectionAsync(IKeyProtector protector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(protector);
        var old = KeyFile.Read(paths.KeyFile);
        var dek = key.Copy();
        try
        {
            KeyFile.Write(paths.KeyFile, await protector.ProtectAsync(dek, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }

        if (old is not null && _protectors.FirstOrDefault(p => p.Method == old.Method) is { } previous)
        {
            try
            {
                await previous.ForgetAsync(old, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                // The new seal is written and works; only the old copy stays behind (for example in the keyring).
                logger.LogWarning(ex, "The old key seal ({Method}) could not be removed; the key may still be in the keyring", old.Method);
            }
        }

        logger.LogInformation("Key protection changed to {Method}", protector.Method);
    }

    /// <summary>
    /// Installs a database file with its key (used by restore): the file is copied in and checked against the current schema, and only then
    /// is the key file written (sealed with this computer's protector). When the schema step fails, the copied file is removed again and no key
    /// file is written, so the folder stays "not created" and another restore can be tried.
    /// </summary>
    public async Task InstallAsync(string databaseFile, byte[] dek, IKeyProtector protector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(protector);
        if (State == VaultState.Open)
        {
            throw new InvalidOperationException("Close the database before replacing it.");
        }

        if (File.Exists(paths.KeyFile))
        {
            throw new InvalidOperationException("A key file already exists; a backup is installed only into an empty data folder.");
        }

        var envelope = await protector.ProtectAsync(dek, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(paths.DatabaseFile))!);
        File.Copy(databaseFile, paths.DatabaseFile, overwrite: true);
        key.Load(dek);

        // The copy is checked against this version's schema before anything uses it (a backup of another version is refused). The key
        // file comes after it.
        try
        {
            await using (var db = await new ConceptDatabase(paths, key).OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                await ConceptDatabase.EnsureSchemaAsync(db, cancellationToken).ConfigureAwait(false);
            }

            KeyFile.Write(paths.KeyFile, envelope);
        }
        catch (Exception ex)
        {
            key.Clear();
            logger.LogError(ex, "The restored database does not have this version's schema; it is removed again and nothing is installed");
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                // The removed file is this restore's own copy of the backup (the backup file itself stays where it was).
                File.Delete(paths.DatabaseFile);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                logger.LogError(cleanup, "The copy of a failed restore could not be removed; it is a database without a key file and is moved aside at the next start");
            }

            try
            {
                await protector.ForgetAsync(envelope, CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException forget)
            {
                logger.LogWarning(forget, "The key seal of a failed restore could not be removed ({Method})", protector.Method);
            }

            throw;
        }

        logger.LogInformation("Database installed from a backup (key protection: {Method})", protector.Method);
    }

    /// <summary>
    /// Sets aside a database this version does not open (<see cref="Database.DatabaseVersionException"/>): the database file and its key file
    /// are renamed in the data folder (".set-aside-&lt;UTC time&gt;"), never deleted, and the app starts fresh (first start). The key seal is
    /// kept, so the version that made the file can still open it when both files get their names back. Returns the new name of the database.
    /// </summary>
    public string SetAside()
    {
        if (State == VaultState.Open)
        {
            throw new InvalidOperationException("Close the database before setting it aside.");
        }

        key.Clear();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var suffix = ".set-aside-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var database = paths.DatabaseFile + suffix;
        if (File.Exists(paths.DatabaseFile))
        {
            File.Move(paths.DatabaseFile, database);
        }

        if (File.Exists(paths.KeyFile))
        {
            File.Move(paths.KeyFile, paths.KeyFile + suffix);
        }

        logger.LogWarning("A database this version does not open was set aside ({Name}); the app starts fresh", Path.GetFileName(database));
        return database;
    }

    /// <summary>Forgets the key in memory (the database is locked again).</summary>
    public void Lock() => key.Clear();
}
