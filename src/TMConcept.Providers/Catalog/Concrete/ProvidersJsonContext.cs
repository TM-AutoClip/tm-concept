using System.Text.Json.Serialization;
using TMConcept.Core.Models;

namespace TMConcept.Providers.Catalog;

/// <summary>Source-generated JSON of the catalogs and the price tables.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ModelCatalogFile))]
[JsonSerializable(typeof(PriceTable))]
internal sealed partial class ProvidersJsonContext : JsonSerializerContext;
