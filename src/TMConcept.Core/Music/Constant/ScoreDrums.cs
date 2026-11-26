namespace TMConcept.Core.Music;

/// <summary>Which drums a style plays (0.1.55, "Program composes").</summary>
public enum ScoreDrums
{
    /// <summary>No drums (a high-energy mood still adds the soft cinematic drums).</summary>
    None,

    /// <summary>Kick and toms on the strong beats, no cymbals (film and orchestra).</summary>
    Cinematic,

    /// <summary>A kick on every beat, hi-hats between (electronic).</summary>
    FourOnTheFloor,

    /// <summary>Kick on 1 and 3, snare on 2 and 4, hi-hats (pop, acoustic).</summary>
    BackBeat,

    /// <summary>Hand drums and a tambourine (world).</summary>
    Hand,
}
