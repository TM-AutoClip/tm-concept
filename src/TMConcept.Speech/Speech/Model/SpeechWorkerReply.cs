namespace TMConcept.Speech.Speech;

/// <summary>The worker's answer line: the clip length, or a failure (<see cref="Code"/> "engine" or "model", <see cref="Error"/> the reason).</summary>
public sealed record SpeechWorkerReply(bool Ok, long DurationMs, string Code, string Error);
