namespace TMConcept.Core.Subtitles;

/// <summary>A scene's narration and when it is heard.</summary>
public sealed record SubtitleSource(long StartMs, long DurationMs, string Text);
