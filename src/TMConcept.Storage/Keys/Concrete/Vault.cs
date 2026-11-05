using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
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

            await using (var db = new ConceptDbContextFactory(paths, key).CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
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
            await using var db = new ConceptDbContextFactory(paths, key).CreateDbContext();
            await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            await db.Settings.CountAsync(cancellationToken).ConfigureAwait(false);
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
            await previous.ForgetAsync(old, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Key protection changed to {Method}", protector.Method);
    }

    /// <summary>
    /// Installs a database file with its key (used by restore): the file is copied in, the key is sealed with this computer's protector.
    /// </summary>
    public async Task InstallAsync(string databaseFile, byte[] dek, IKeyProtector protector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(protector);
        if (State == VaultState.Open)
        {
            throw new InvalidOperationException("Close the database before replacing it.");
        }

        var envelope = await protector.ProtectAsync(dek, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(paths.DatabaseFile))!);
        File.Copy(databaseFile, paths.DatabaseFile, overwrite: true);
        KeyFile.Write(paths.KeyFile, envelope);
        key.Load(dek);
        logger.LogInformation("Database installed from a backup (key protection: {Method})", protector.Method);
    }

    /// <summary>Forgets the key in memory (the database is locked again).</summary>
    public void Lock() => key.Clear();
}
