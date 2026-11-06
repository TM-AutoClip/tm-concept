namespace TMConcept.Storage.Backup;

/// <summary>Backup rules (decision: daily, at the first start of the day, the last ten kept).</summary>
public static class BackupRules
{
    /// <summary>File extension of backups.</summary>
    public const string Extension = ".tmcbak";

    /// <summary>File name prefix of automatic backups.</summary>
    public const string Prefix = "tm-concept-";

    /// <summary>File name prefix of the backups written with "Back up now" (their own rotation; they never count as the daily backup).</summary>
    public const string ManualPrefix = "tm-concept-manual-";

    /// <summary>File name prefix of the backups taken before a large change (kept apart from the daily ones).</summary>
    public const string ChangePrefix = "tm-concept-change-";

    /// <summary>Backups kept in the backup folder.</summary>
    public const int Keep = 10;

    /// <summary>Deleting this many concepts for good (or a whole profile) takes a backup first.</summary>
    public const int BulkChange = 20;
}
