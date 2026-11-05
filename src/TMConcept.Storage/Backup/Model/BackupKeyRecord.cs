namespace TMConcept.Storage.Backup;

/// <summary>
/// The backup key derived from the backup password (kept in the encrypted database, never the password itself). It lets the app write
/// daily backups without asking; opening a backup always needs the password.
/// </summary>
public sealed class BackupKeyRecord
{
    /// <summary>Argon2id salt (base64).</summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>Argon2id memory (KiB).</summary>
    public int MemoryKiB { get; set; }

    /// <summary>Argon2id passes.</summary>
    public int Iterations { get; set; }

    /// <summary>Argon2id lanes.</summary>
    public int Parallelism { get; set; }

    /// <summary>Derived key (base64).</summary>
    public string Key { get; set; } = string.Empty;
}
