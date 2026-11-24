namespace TMConcept.Core.Subtitles;

/// <summary>
/// A word of the narration text (always the script's own spelling) with the time it is said. <see cref="Interpolated"/>: the recogniser did
/// not hear it, so its time was placed between its neighbours.
/// </summary>
public sealed record TimedWord(string Text, long StartMs, long EndMs, bool Interpolated = false);
