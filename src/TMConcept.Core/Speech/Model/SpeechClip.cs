namespace TMConcept.Core.Speech;

/// <summary>A spoken file: length, characters, cost (online engines; an estimate from the price per character).</summary>
public sealed record SpeechClip(string File, long DurationMs, int Characters, decimal CostUsd, string Engine);
