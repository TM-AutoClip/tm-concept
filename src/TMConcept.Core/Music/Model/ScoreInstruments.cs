namespace TMConcept.Core.Music;

/// <summary>
/// The instruments of a style (0.1.55, "Program composes"; one per <see cref="ScoreStyle"/>, <see cref="ScoreStyles"/>): General
/// MIDI programs of the held chords, the broken chords, the bass and the tune (-1 = no tune), and which drums it plays.
/// </summary>
/// <param name="Pad">The held chords.</param>
/// <param name="Arpeggio">The broken chords.</param>
/// <param name="Bass">The bass.</param>
/// <param name="Lead">The tune (-1 = none).</param>
/// <param name="Drums">The drums.</param>
public sealed record ScoreInstruments(int Pad, int Arpeggio, int Bass, int Lead, ScoreDrums Drums);
