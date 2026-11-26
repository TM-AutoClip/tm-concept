namespace TMConcept.Core.Music;

/// <summary>
/// The moods of composed music (0.1.55, "Program composes"): one per <see cref="MusicTags.Moods"/> word, in the same order (the name in lower case
/// is the word).
/// </summary>
public enum ScoreMood
{
    /// <summary>Calm (the default).</summary>
    Calm,

    /// <summary>Uplifting.</summary>
    Uplifting,

    /// <summary>Epic.</summary>
    Epic,

    /// <summary>Dark.</summary>
    Dark,

    /// <summary>Mysterious.</summary>
    Mysterious,

    /// <summary>Playful.</summary>
    Playful,

    /// <summary>Tense.</summary>
    Tense,

    /// <summary>Sad.</summary>
    Sad,

    /// <summary>Action.</summary>
    Action,
}
