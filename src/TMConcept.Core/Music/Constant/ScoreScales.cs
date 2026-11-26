namespace TMConcept.Core.Music;

/// <summary>The scales of composed music (0.1.55, "Program composes"), as semitones above the key's root.</summary>
public static class ScoreScales
{
    /// <summary>Major.</summary>
    public static readonly IReadOnlyList<int> Major = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>Natural minor.</summary>
    public static readonly IReadOnlyList<int> Minor = [0, 2, 3, 5, 7, 8, 10];

    /// <summary>The scale colour of each flavour style: Hicaz (phrygian dominant) for world, dorian for medieval.</summary>
    public static readonly IReadOnlyDictionary<ScoreStyle, IReadOnlyList<int>> Flavors = new Dictionary<ScoreStyle, IReadOnlyList<int>>
    {
        [ScoreStyle.World] = [0, 1, 4, 5, 7, 8, 10],
        [ScoreStyle.Medieval] = [0, 2, 3, 5, 7, 9, 10],
    };
}
