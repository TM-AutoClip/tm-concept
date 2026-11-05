namespace TMConcept.Storage.Backup;

/// <summary>Backup rules (decision: daily, at the first start of the day, the last ten kept).</summary>
public static class BackupRules
{
    /// <summary>File extension of backups.</summary>
    public const string Extension = ".tmcbak";

    /// <summary>File name prefix of automatic backups.</summary>
    public const string Prefix = "tm-concept-";

    /// <summary>Backups kept in the backup folder.</summary>
    public const int Keep = 10;

    /// <summary>A bulk change this large (concepts added or deleted) triggers an extra backup.</summary>
    public const int BulkChange = 20;
}
