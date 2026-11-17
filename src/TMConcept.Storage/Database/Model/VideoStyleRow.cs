namespace TMConcept.Storage.Database;

/// <summary>
/// A template as stored (<c>video_styles</c>): its id, name and times as columns, everything else as one JSON document
/// (<see cref="Core.Render.CoreJsonContext"/>), so a field added to <see cref="Core.Styles.VideoStyle"/> later needs no schema step.
/// </summary>
public sealed class VideoStyleRow
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Name (the list is sorted by it).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The whole template as JSON.</summary>
    public string Json { get; set; } = string.Empty;

    /// <summary>Created (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last change (UTC).</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
