namespace TMConcept.Core.Profiles;

/// <summary>A profile = a channel: what kind of videos it makes, in which language and format, with which voice.</summary>
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

    /// <summary>Speech engine code (empty = no narration).</summary>
    public string SpeechEngine { get; set; } = string.Empty;

    /// <summary>Voice id of the speech engine.</summary>
    public string SpeechVoice { get; set; } = string.Empty;

    /// <summary>Speech speed (1 = normal).</summary>
    public double SpeechSpeed { get; set; } = 1.0;

    /// <summary>Last concept number given in this profile (numbers are never reused, even after a permanent delete).</summary>
    public int ConceptCounter { get; set; }

    /// <summary>Created (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last change (UTC).</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
