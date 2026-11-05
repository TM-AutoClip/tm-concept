using System.Text.Json.Serialization;

namespace TMConcept.Speech.Speech;

/// <summary>Model-specific generation options sherpa-onnx takes as JSON (Supertonic: the language code).</summary>
internal sealed record SpeechWorkerExtra([property: JsonPropertyName("lang")] string Lang);
