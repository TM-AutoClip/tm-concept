namespace TMConcept.Core.Music;

/// <summary>
/// The instruments of each <see cref="ScoreStyle"/> (0.1.55, "Program composes"), as General MIDI programs (counted from 0: 48 string
/// ensemble, 46 harp, 43 contrabass, 73 flute …), so any General MIDI SoundFont plays them.
/// </summary>
public static class ScoreStyles
{
    /// <summary>The style of a prompt without a style word.</summary>
    public const ScoreStyle Default = ScoreStyle.Cinematic;

    /// <summary>The styles that only colour the scale when another style is asked for too ("Egyptian disco": electronic instruments, an eastern scale).</summary>
    public static readonly IReadOnlyList<ScoreStyle> Flavors = [ScoreStyle.World, ScoreStyle.Medieval];

    /// <summary>The instruments of each style.</summary>
    public static readonly IReadOnlyDictionary<ScoreStyle, ScoreInstruments> All = new Dictionary<ScoreStyle, ScoreInstruments>
    {
        [ScoreStyle.Orchestral] = new(Pad: 48, Arpeggio: 46, Bass: 43, Lead: 73, ScoreDrums.None),
        [ScoreStyle.Cinematic] = new(Pad: 49, Arpeggio: 0, Bass: 43, Lead: 60, ScoreDrums.Cinematic),
        [ScoreStyle.Electronic] = new(Pad: 89, Arpeggio: 81, Bass: 38, Lead: 80, ScoreDrums.FourOnTheFloor),
        [ScoreStyle.Ambient] = new(Pad: 88, Arpeggio: 98, Bass: 39, Lead: -1, ScoreDrums.None),
        [ScoreStyle.Acoustic] = new(Pad: 48, Arpeggio: 25, Bass: 32, Lead: 0, ScoreDrums.BackBeat),
        [ScoreStyle.World] = new(Pad: 48, Arpeggio: 46, Bass: 32, Lead: 111, ScoreDrums.Hand),
        [ScoreStyle.Medieval] = new(Pad: 52, Arpeggio: 46, Bass: 70, Lead: 74, ScoreDrums.None),
        [ScoreStyle.Pop] = new(Pad: 89, Arpeggio: 4, Bass: 33, Lead: 11, ScoreDrums.BackBeat),
    };
}
