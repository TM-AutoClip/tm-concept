namespace TMConcept.Core.Music;

/// <summary>
/// The numbers of the app's own composed music (0.1.55, "Program composes"): the catalog id, the MIDI grid, what a score may hold
/// (<see cref="ScoreValidator"/>) and the ranges the composer writes in (<see cref="ProgramScoreWriter"/>).
/// </summary>
public static class ScoreRules
{
    /// <summary>The catalog id of the option ("local/program-score").</summary>
    public const string ModelId = "program-score";

    /// <summary>The half steps of an octave.</summary>
    public const int Octave = 12;

    /// <summary>The notes of a scale (and so the scale steps of an octave).</summary>
    public const int ScaleLength = 7;

    /// <summary>The seconds of a minute (a tempo is beats per minute).</summary>
    public const double SecondsPerMinute = 60.0;

    /// <summary>The shortest piece the composer writes (seconds).</summary>
    public const int MinSeconds = 1;

    /// <summary>The softest velocity a note may have (0 would be a note-off).</summary>
    public const int MinVelocity = 1;

    /// <summary>The ticks of a quarter note.</summary>
    public const int TicksPerQuarter = 480;

    /// <summary>The quarter notes of a bar (4/4).</summary>
    public const int BeatsPerBar = 4;

    /// <summary>The slowest tempo a score may have.</summary>
    public const int MinBpm = 40;

    /// <summary>The fastest tempo a score may have.</summary>
    public const int MaxBpm = 200;

    /// <summary>The longest score (seconds).</summary>
    public const int MaxSeconds = 600;

    /// <summary>The most tracks of a score (one per MIDI channel).</summary>
    public const int MaxTracks = 16;

    /// <summary>The most notes of a score.</summary>
    public const int MaxNotes = 40_000;

    /// <summary>The highest MIDI channel number.</summary>
    public const int MaxChannel = 15;

    /// <summary>The highest MIDI data value (note, velocity, program, controller value).</summary>
    public const int MaxData = 127;

    /// <summary>The General MIDI drum channel (channel 10, counted from 0).</summary>
    public const int DrumChannel = 9;

    /// <summary>The bars before the tune comes in (held chords and the bass only).</summary>
    public const int IntroBars = 2;

    /// <summary>The bars of one section (the tune changes between sections).</summary>
    public const int SectionBars = 8;

    /// <summary>The bars of the repeated tune figure.</summary>
    public const int MotifBars = 2;

    /// <summary>The lowest note of the bass.</summary>
    public const int BassLow = 36;

    /// <summary>The lowest note of the held chords' root.</summary>
    public const int PadLow = 52;

    /// <summary>The lowest note of the broken chords.</summary>
    public const int ArpeggioLow = 60;

    /// <summary>The lowest note of the tune.</summary>
    public const int LeadLow = 64;

    /// <summary>The highest note of the tune.</summary>
    public const int LeadHigh = 84;

    /// <summary>The keys the seed chooses from (pitch classes: C, D, E flat, F, G, A).</summary>
    public static readonly IReadOnlyList<int> Keys = [0, 2, 3, 5, 7, 9];

    /// <summary>The softest velocity the composer writes.</summary>
    public const int SoftVelocity = 40;

    /// <summary>How much louder the loudest mood plays than <see cref="SoftVelocity"/>.</summary>
    public const int VelocityRange = 50;

    /// <summary>The energy from which the drums play in a style without drums of its own.</summary>
    public const double DrumEnergy = 0.75;

    /// <summary>The energy from which the bass plays eighth notes.</summary>
    public const double BusyEnergy = 0.7;

    /// <summary>The energy below which the bass holds a whole bar and the broken chords play quarter notes.</summary>
    public const double QuietEnergy = 0.4;

    /// <summary>The reverb amount of every track (MIDI controller 91).</summary>
    public const int Reverb = 48;

    /// <summary>The middle between the speakers.</summary>
    public const int Center = 64;

    /// <summary>The tempo a prompt's "slow" word asks for.</summary>
    public const int SlowBpm = 72;

    /// <summary>The tempo a prompt's "fast" word asks for.</summary>
    public const int FastBpm = 132;
}
