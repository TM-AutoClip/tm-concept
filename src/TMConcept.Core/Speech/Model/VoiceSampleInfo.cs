namespace TMConcept.Core.Speech;

/// <summary>A voice sample without its sound (for lists and the voice pickers).</summary>
public sealed record VoiceSampleInfo(Guid Id, string Name, long DurationMs, DateTimeOffset AddedAtUtc);
