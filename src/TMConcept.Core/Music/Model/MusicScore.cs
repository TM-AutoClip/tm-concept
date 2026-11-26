namespace TMConcept.Core.Music;

/// <summary>
/// A composed piece (0.1.55, "Program composes"): its tempo, its grid (ticks per quarter note, beats per bar, bars) and its instruments. Written
/// as a MIDI file by <see cref="MidiWriter"/> and played by a SoundFont synthesizer (<see cref="IMidiRenderer"/>); checked by
/// <see cref="ScoreValidator"/> first.
/// </summary>
/// <param name="Bpm">The tempo (quarter notes a minute).</param>
/// <param name="TicksPerQuarter">The ticks of a quarter note.</param>
/// <param name="BeatsPerBar">The quarter notes of a bar (4 = 4/4).</param>
/// <param name="Bars">How many bars the piece has.</param>
/// <param name="Tracks">The instruments.</param>
public sealed record MusicScore(int Bpm, int TicksPerQuarter, int BeatsPerBar, int Bars, IReadOnlyList<ScoreTrack> Tracks)
{
    /// <summary>The tick the piece ends on.</summary>
    public int EndTick => Bars * BeatsPerBar * TicksPerQuarter;

    /// <summary>How long the piece is (seconds, without the instruments' fading tail).</summary>
    public double Seconds => Bars * BeatsPerBar * 60.0 / Bpm;
}
