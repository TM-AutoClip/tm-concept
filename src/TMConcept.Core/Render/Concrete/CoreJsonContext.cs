using System.Text.Json.Serialization;
using TMConcept.Core.Templates;

namespace TMConcept.Core.Render;

/// <summary>Source-generated JSON of Core's documents (no reflection; works with trimming).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RenderInput))]
[JsonSerializable(typeof(ChannelTemplate))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
