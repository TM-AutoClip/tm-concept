namespace TMConcept.Core.Music;

/// <summary>One drum of a bar's pattern (0.1.55, <see cref="ScoreDrumPatterns"/>): its sound, the eighth notes it falls on, how much softer than the bar's velocity it plays and from which energy it plays at all.</summary>
/// <param name="Note">The General MIDI drum sound (<see cref="ScoreDrumNotes"/>).</param>
/// <param name="Eighths">The eighth notes of the bar it falls on (0–7).</param>
/// <param name="Softer">How much softer it plays than the bar's velocity.</param>
/// <param name="MinEnergy">The mood's energy from which it plays (0 = always).</param>
public sealed record DrumHit(int Note, IReadOnlyList<int> Eighths, int Softer, double MinEnergy);
