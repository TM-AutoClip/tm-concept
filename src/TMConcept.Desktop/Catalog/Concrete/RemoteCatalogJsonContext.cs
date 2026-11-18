using System.Text.Json.Serialization;

namespace TMConcept.Desktop.Catalog;

/// <summary>
/// JSON of the remote model catalog: camelCase names, and a field the app does not know is an error (the whole file is refused, so a
/// newer file's field is never silently dropped).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = true)]
[JsonSerializable(typeof(RemoteCatalogFile))]
internal sealed partial class RemoteCatalogJsonContext : JsonSerializerContext;
