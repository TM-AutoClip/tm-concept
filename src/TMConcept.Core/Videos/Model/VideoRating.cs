namespace TMConcept.Core.Videos;

/// <summary>
/// The user's like or dislike of a made video: one per video (all its versions share it), found by its concept or, when there is none, by
/// its folder. The title and summary are copies, so the rating keeps steering the topic and concept prompts after the concept or the video
/// is deleted. Deleted with the profile; a backup holds it with the concepts.
/// </summary>
public sealed class VideoRating
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Profile.</summary>
    public Guid ProfileId { get; set; }

    /// <summary>The concept the video was made from (null when it was deleted, or the video had none).</summary>
    public Guid? ConceptId { get; set; }

    /// <summary>The video's folder relative to the library, without its version folder (v1, v2…): every version shares it.</summary>
    public string VideoGroup { get; set; } = string.Empty;

    /// <summary>Title copy.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Summary copy (the concept's two sentences; empty when the concept was already gone).</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Like or dislike.</summary>
    public VideoVote Vote { get; set; } = VideoVote.Like;

    /// <summary>When it was rated (UTC).</summary>
    public DateTimeOffset RatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
