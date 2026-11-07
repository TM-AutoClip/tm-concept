namespace TMConcept.Speech.Speech;

/// <summary>
/// One line the app sends to the worker: speak <see cref="Text"/> into <see cref="OutputWav"/>, or (with <see cref="Texts"/>) turn texts into
/// vectors with the local embedding model. <see cref="ReferenceWav"/>: the voice sample a voice that copies a voice (Pocket TTS) speaks
/// with (a mono 16-bit WAV file the worker reads itself; only its samples reach the native library).
/// </summary>
public sealed record SpeechWorkerRequest(string Text, string Language, int Speaker, float Speed, int Steps, string OutputWav,
    IReadOnlyList<string>? Texts = null, string? ReferenceWav = null);
