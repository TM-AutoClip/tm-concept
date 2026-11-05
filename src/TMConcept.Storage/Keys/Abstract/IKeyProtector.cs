namespace TMConcept.Storage.Keys;

/// <summary>
/// Protects the 32-byte database key with something of the operating system (or a master password). The database key itself never touches
/// the disk unprotected.
/// </summary>
public interface IKeyProtector
{
    /// <summary>Method name (<see cref="KeyMethods"/>).</summary>
    string Method { get; }

    /// <summary>Whether this protector works on this computer now.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether it opens without asking the user (an operating system keyring) or asks for a master password.</summary>
    bool Automatic { get; }

    /// <summary>Seals the key.</summary>
    Task<KeyEnvelope> ProtectAsync(byte[] key, CancellationToken cancellationToken);

    /// <summary>Opens the sealed key; an exception when it cannot.</summary>
    Task<byte[]> UnprotectAsync(KeyEnvelope envelope, CancellationToken cancellationToken);

    /// <summary>Removes what the protector stored outside the key file (keyring entry); nothing for the others.</summary>
    Task ForgetAsync(KeyEnvelope envelope, CancellationToken cancellationToken);
}
