using System.Text.Json.Serialization;

namespace TMConcept.Desktop.Catalog;

/// <summary>
/// JSON of the remote model catalog: camelCase names, and a field the app does not know is an error (the whole file is refused, so a
/// newer file's field is never silently dropped). A null where the model has no nullable field (<c>"id": null</c>) is an error too, so the
/// checks never meet a null string (list items are checked by the reader: the annotations do not reach them).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true, WriteIndented = true)]
[JsonSerializable(typeof(RemoteCatalogFile))]
internal sealed partial class RemoteCatalogJsonContext : JsonSerializerContext;
