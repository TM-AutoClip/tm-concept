namespace TMConcept.Speech.Speech;

/// <summary>One line the app sends to the speech worker: speak <see cref="Text"/> into <see cref="OutputWav"/>.</summary>
public sealed record SpeechWorkerRequest(string Text, string Language, int Speaker, float Speed, int Steps, string OutputWav);
