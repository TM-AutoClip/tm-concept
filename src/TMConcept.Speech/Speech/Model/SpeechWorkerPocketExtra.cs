using System.Text.Json.Serialization;

namespace TMConcept.Speech.Speech;

/// <summary>
/// The generation options of Pocket TTS as sherpa-onnx takes them (JSON): how many seconds of the voice sample are used (the whole sample:
/// samples are cut to that length when they are added) and a fixed seed, so every scene of a video gets the same voice.
/// </summary>
internal sealed record SpeechWorkerPocketExtra(
    [property: JsonPropertyName("max_reference_audio_len")] float MaxReferenceSeconds,
    [property: JsonPropertyName("seed")] int Seed);
