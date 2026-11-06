namespace TMConcept.Core.Profiles;

/// <summary>
/// A profile = a channel: what kind of videos it makes, in which language and format. Its models (voice, images, engine…) are step
/// choices kept next to it (<see cref="Models.IStepModelStore"/>): the last models used on each page.
/// </summary>
public sealed class Profile
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Channel name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Language of concepts and narration (<see cref="ContentLanguages"/>).</summary>
    public string ContentLanguage { get; set; } = ContentLanguages.English;

    /// <summary>Vertical or horizontal.</summary>
    public VideoFormat Format { get; set; } = VideoFormat.Vertical;

    /// <summary>Target video length in seconds.</summary>
    public int TargetSeconds { get; set; } = 75;

    /// <summary>The channel's concept: vision, goal, what the videos are like, tone, what to avoid.</summary>
    public string ConceptText { get; set; } = string.Empty;

    /// <summary>The template the fields were filled from (information only; the template never changes the profile later).</summary>
    public string? TemplateId { get; set; }

    /// <summary>Last concept number given in this profile (numbers are never reused, even after a permanent delete).</summary>
    public int ConceptCounter { get; set; }

    /// <summary>Created (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last change (UTC).</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
