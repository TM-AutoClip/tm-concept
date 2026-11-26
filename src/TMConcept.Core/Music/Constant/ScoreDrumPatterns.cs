namespace TMConcept.Core.Music;

/// <summary>The one-bar drum patterns of composed music (0.1.55, "Program composes"), in eighth notes of a 4/4 bar.</summary>
public static class ScoreDrumPatterns
{
    /// <summary>The drums of each pattern (<see cref="ScoreDrums.None"/> has none).</summary>
    public static readonly IReadOnlyDictionary<ScoreDrums, IReadOnlyList<DrumHit>> All = new Dictionary<ScoreDrums, IReadOnlyList<DrumHit>>
    {
        [ScoreDrums.None] = [],
        [ScoreDrums.Cinematic] =
        [
            new(ScoreDrumNotes.Kick, [0, 4], 0, 0),
            new(ScoreDrumNotes.LowTom, [4], 10, 0),
            new(ScoreDrumNotes.LowTom, [7], 15, 0.8),
        ],
        [ScoreDrums.FourOnTheFloor] =
        [
            new(ScoreDrumNotes.Kick, [0, 2, 4, 6], 0, 0),
            new(ScoreDrumNotes.ClosedHat, [1, 3, 5, 7], 25, 0),
            new(ScoreDrumNotes.Snare, [2, 6], 10, 0.6),
            new(ScoreDrumNotes.OpenHat, [7], 30, 0.8),
        ],
        [ScoreDrums.BackBeat] =
        [
            new(ScoreDrumNotes.Kick, [0, 4], 0, 0),
            new(ScoreDrumNotes.Kick, [5], 10, 0.7),
            new(ScoreDrumNotes.SideStick, [2, 6], 10, 0),
            new(ScoreDrumNotes.Snare, [2, 6], 5, 0.5),
            new(ScoreDrumNotes.ClosedHat, [0, 2, 4, 6], 25, 0),
            new(ScoreDrumNotes.ClosedHat, [1, 3, 5, 7], 30, 0.5),
        ],
        [ScoreDrums.Hand] =
        [
            new(ScoreDrumNotes.LowConga, [0, 4], 0, 0),
            new(ScoreDrumNotes.HighConga, [3, 6, 7], 10, 0),
            new(ScoreDrumNotes.Tambourine, [0, 1, 2, 3, 4, 5, 6, 7], 30, 0.4),
        ],
    };
}
