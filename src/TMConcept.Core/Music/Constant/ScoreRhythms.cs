namespace TMConcept.Core.Music;

/// <summary>
/// The tune's one-bar rhythms (0.1.55, "Program composes"), as note lengths in eighth notes adding up to a 4/4 bar; the seed picks the figure's
/// rhythms from them, and a phrase's last bar ends on <see cref="PhraseEnd"/>.
/// </summary>
public static class ScoreRhythms
{
    /// <summary>The eighth notes of a bar.</summary>
    public const int EighthsPerBar = 8;

    /// <summary>Every fourth bar of a section closes the phrase.</summary>
    public const int PhraseBars = 4;

    /// <summary>The figure rhythms.</summary>
    public static readonly IReadOnlyList<IReadOnlyList<int>> Cells =
    [
        [2, 2, 2, 2],
        [3, 1, 2, 2],
        [4, 2, 2],
        [2, 2, 4],
        [1, 1, 2, 4],
        [6, 2],
        [2, 1, 1, 4],
        [3, 3, 2],
    ];

    /// <summary>The closing bar of a phrase: a held note, then a rest.</summary>
    public static readonly IReadOnlyList<int> PhraseEnd = [6];

    /// <summary>The steps (in scale notes) the tune moves between two notes.</summary>
    public static readonly IReadOnlyList<int> Steps = [-2, -1, -1, 0, 1, 1, 2];

    /// <summary>The share of a figure's notes (not its first) that may be rests in the calmest mood (less as the energy rises).</summary>
    public const double RestShare = 0.25;
}
