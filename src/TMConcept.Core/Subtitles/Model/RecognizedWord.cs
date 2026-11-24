namespace TMConcept.Core.Subtitles;

/// <summary>A word as the recogniser heard it, with its time in the heard file (milliseconds from the file's start).</summary>
public sealed record RecognizedWord(string Text, long StartMs, long EndMs);
