namespace TMConcept.Core.Music;

/// <summary>
/// The music a scene asks for (0.1.45, C4): written by the scene plan call next to the scene's plan (no call is added) and read by
/// <see cref="MusicMatcher"/> for a template whose library music is chosen by the concept. Every field may be empty.
/// </summary>
public sealed class MusicHint
{
    /// <summary>The scene's mood (<see cref="MusicTags.Moods"/>).</summary>
    public string Mood { get; set; } = string.Empty;

    /// <summary>The style that fits it (<see cref="MusicTags.Styles"/>).</summary>
    public string Style { get; set; } = string.Empty;

    /// <summary>Its tempo (<see cref="MusicTags.Tempos"/>).</summary>
    public string Tempo { get; set; } = string.Empty;

    /// <summary>The music starts with this scene (before it the video has none).</summary>
    public bool Start { get; set; }

    /// <summary>The music ends with this scene.</summary>
    public bool End { get; set; }

    /// <summary>How the music comes in or goes out here (<see cref="MusicTags.Transitions"/>).</summary>
    public string Transition { get; set; } = string.Empty;
}
