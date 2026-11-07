using System.Text.Json.Serialization;

namespace TMConcept.Speech.Speech;

/// <summary>Source-generated JSON of the speech worker's lines (no reflection, so trimming keeps it working).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SpeechWorkerRequest))]
[JsonSerializable(typeof(SpeechWorkerReply))]
[JsonSerializable(typeof(SpeechWorkerExtra))]
[JsonSerializable(typeof(SpeechWorkerPocketExtra))]
internal sealed partial class SpeechWorkerJsonContext : JsonSerializerContext;
