namespace TMConcept.Core.Music;

/// <summary>
/// One instrument of a composed piece (0.1.55, "Program composes"): its name, its MIDI channel (<see cref="ScoreRules.DrumChannel"/> = the drum
/// kit), its General MIDI instrument, its loudness and place between the speakers, and its notes.
/// </summary>
/// <param name="Name">The track's name (written into the MIDI file).</param>
/// <param name="Channel">The MIDI channel (0–15).</param>
/// <param name="Program">The General MIDI instrument (0–127; not used on the drum channel).</param>
/// <param name="Volume">The channel volume (0–127).</param>
/// <param name="Pan">The place between the speakers (0 = left, 64 = middle, 127 = right).</param>
/// <param name="Notes">The notes.</param>
public sealed record ScoreTrack(string Name, int Channel, int Program, int Volume, int Pan, IReadOnlyList<ScoreNote> Notes);
