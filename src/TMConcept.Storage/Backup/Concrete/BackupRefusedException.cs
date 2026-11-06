namespace TMConcept.Storage.Backup;

/// <summary>
/// A backup opened (the password was right) but cannot be restored by this version, for example because a newer version wrote it. Nothing
/// in the data folder was changed; the message says why.
/// </summary>
public sealed class BackupRefusedException : Exception
{
    /// <summary>Creates.</summary>
    public BackupRefusedException()
    {
    }

    /// <summary>With a message.</summary>
    public BackupRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>With a message and the inner error.</summary>
    public BackupRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
