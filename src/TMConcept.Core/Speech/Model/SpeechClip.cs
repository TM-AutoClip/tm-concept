namespace TMConcept.Core.Speech;

/// <summary>
/// A spoken file: length, characters, cost (online engines; an estimate from the price per character), and the words a dictionary voice
/// (Kokoro) did not know and left out.
/// </summary>
public sealed record SpeechClip(string File, long DurationMs, int Characters, decimal CostUsd, string Engine)
{
    /// <summary>Words the voice left out because its dictionary does not hold them (lowercase; empty for other voices).</summary>
    public IReadOnlyList<string> SkippedWords { get; init; } = [];
}
