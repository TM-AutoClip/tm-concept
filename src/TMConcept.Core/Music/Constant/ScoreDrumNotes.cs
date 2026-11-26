namespace TMConcept.Core.Music;

/// <summary>The General MIDI drum sounds the composer uses (note numbers on <see cref="ScoreRules.DrumChannel"/>).</summary>
public static class ScoreDrumNotes
{
    /// <summary>Bass drum.</summary>
    public const int Kick = 36;

    /// <summary>Side stick (the soft snare).</summary>
    public const int SideStick = 37;

    /// <summary>Snare.</summary>
    public const int Snare = 38;

    /// <summary>Closed hi-hat.</summary>
    public const int ClosedHat = 42;

    /// <summary>Open hi-hat.</summary>
    public const int OpenHat = 46;

    /// <summary>Low tom.</summary>
    public const int LowTom = 45;

    /// <summary>Crash cymbal (the start of a section).</summary>
    public const int Crash = 49;

    /// <summary>Tambourine.</summary>
    public const int Tambourine = 54;

    /// <summary>Open high conga.</summary>
    public const int HighConga = 63;

    /// <summary>Low conga.</summary>
    public const int LowConga = 64;
}
