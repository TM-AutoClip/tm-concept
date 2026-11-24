namespace TMConcept.Core.Videos;

/// <summary>
/// A note left on a made video while it plays (F16, 0.1.49): the moment it was written at (milliseconds in the video), the scene at that moment
/// (1-based, from the version's timing) and the text. Kept in the version folder (<see cref="VideoFolders.NotesFile"/>), not in the database.
/// </summary>
public sealed class TimeNote
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The moment in the video, milliseconds.</summary>
    public long AtMs { get; set; }

    /// <summary>The scene at that moment (1-based).</summary>
    public int Scene { get; set; }

    /// <summary>The note.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>When it was written (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
