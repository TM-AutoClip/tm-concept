using System.Text.Json.Serialization;
using TMConcept.Core.Models;

namespace TMConcept.Providers.Catalog;

/// <summary>Source-generated JSON of the catalogs.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip)]
[JsonSerializable(typeof(ModelCatalogFile))]
internal sealed partial class ProvidersJsonContext : JsonSerializerContext;
