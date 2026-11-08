namespace TMConcept.Core.Speech;

/// <summary>One spoken part of a joined clip: where it starts in the clip, how long it is heard and what it says (for the subtitles).</summary>
public sealed record SpeechSpan(long StartMs, long DurationMs, string Text);
