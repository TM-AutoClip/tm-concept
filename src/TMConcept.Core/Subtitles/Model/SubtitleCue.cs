namespace TMConcept.Core.Subtitles;

/// <summary>One subtitle: time range and at most two lines.</summary>
public sealed record SubtitleCue(long StartMs, long EndMs, IReadOnlyList<string> Lines);
