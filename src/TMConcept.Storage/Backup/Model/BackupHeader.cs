namespace TMConcept.Storage.Backup;

/// <summary>
/// The header inside a format 03 backup (encrypted with the rest): what was included, how long the database copy is, the image files that
/// follow it, and the counts shown before a restore.
/// </summary>
public sealed class BackupHeader
{
    /// <summary>Format version (3).</summary>
    public int Format { get; set; } = 3;

    /// <summary>When the backup was written (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>What is in.</summary>
    public BackupContents Contents { get; set; } = new();

    /// <summary>Length of the encrypted database copy.</summary>
    public long DatabaseLength { get; set; }

    /// <summary>The sample image files after the database, in order.</summary>
    public List<BackupImage> Images { get; set; } = [];

    /// <summary>API keys in the copy.</summary>
    public int KeyCount { get; set; }

    /// <summary>Profiles in the copy.</summary>
    public int ProfileCount { get; set; }

    /// <summary>Concepts in the copy (the trash too).</summary>
    public int ConceptCount { get; set; }

    /// <summary>Video records in the copy.</summary>
    public int VideoCount { get; set; }
}
