namespace TMConcept.Speech.Speech;

/// <summary>
/// One line the app sends to the worker: speak <see cref="Text"/> into <see cref="OutputWav"/>, or (with <see cref="Texts"/>) turn texts into
/// vectors with the local embedding model.
/// </summary>
public sealed record SpeechWorkerRequest(string Text, string Language, int Speaker, float Speed, int Steps, string OutputWav,
    IReadOnlyList<string>? Texts = null);
