namespace TMConcept.Core.Speech;

/// <summary>
/// A spoken file: length, characters, cost (online engines; an estimate from the price per character), and the words a dictionary voice
/// (Kokoro) did not know and left out.
/// </summary>
public sealed record SpeechClip(string File, long DurationMs, int Characters, decimal CostUsd, string Engine)
{
    /// <summary>Words the voice left out because its dictionary does not hold them (lowercase; empty for other voices).</summary>
    public IReadOnlyList<string> SkippedWords { get; init; } = [];

    /// <summary>
    /// The spoken parts of a clip joined with pauses (start in the clip, length, text), so the subtitles follow the voice and wait through a
    /// pause; empty for a clip spoken in one go (its text fills the whole clip).
    /// </summary>
    public IReadOnlyList<SpeechSpan> Spans { get; init; } = [];
}
