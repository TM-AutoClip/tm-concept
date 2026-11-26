namespace TMConcept.Core.Music;

/// <summary>
/// The styles of composed music (0.1.55, "Program composes"): one per <see cref="MusicTags.Styles"/> word, in the same order (the name in lower
/// case is the word).
/// </summary>
public enum ScoreStyle
{
    /// <summary>Orchestral.</summary>
    Orchestral,

    /// <summary>Cinematic (the default).</summary>
    Cinematic,

    /// <summary>Electronic.</summary>
    Electronic,

    /// <summary>Ambient.</summary>
    Ambient,

    /// <summary>Acoustic.</summary>
    Acoustic,

    /// <summary>World (next to another style only an eastern scale colour).</summary>
    World,

    /// <summary>Medieval (next to another style only a dorian scale colour).</summary>
    Medieval,

    /// <summary>Pop.</summary>
    Pop,
}
