namespace TMConcept.Speech.Speech;

/// <summary>
/// The worker's answer line: the clip length (or the vectors of an embedding request), or a failure (<see cref="Code"/> "engine" or
/// "model", <see cref="Error"/> the reason). <see cref="Skipped"/>: the words a dictionary voice (Kokoro) does not know and leaves out.
/// </summary>
public sealed record SpeechWorkerReply(bool Ok, long DurationMs, string Code, string Error, IReadOnlyList<float[]>? Vectors = null,
    IReadOnlyList<string>? Skipped = null);
