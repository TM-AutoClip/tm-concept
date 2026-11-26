namespace TMConcept.Core.Music;

/// <summary>One note of a composed piece (0.1.55, "Program composes"): where it starts and how long it lasts in MIDI ticks, its pitch and its loudness.</summary>
/// <param name="Start">The tick it starts on (0 = the first beat).</param>
/// <param name="Length">How many ticks it lasts (at least one).</param>
/// <param name="Pitch">The MIDI note number (60 = middle C; on the drum channel the drum sound).</param>
/// <param name="Velocity">How hard it is struck (1–127).</param>
public sealed record ScoreNote(int Start, int Length, int Pitch, int Velocity);
