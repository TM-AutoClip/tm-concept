using System.Text.Json.Serialization;

namespace TMConcept.Speech.Python;

/// <summary>Source-generated JSON of the Python worker's lines (snake_case, empty fields left out; no reflection, so trimming keeps it working).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PythonWorkerRequest))]
[JsonSerializable(typeof(PythonWorkerReply))]
internal sealed partial class PythonJsonContext : JsonSerializerContext;
