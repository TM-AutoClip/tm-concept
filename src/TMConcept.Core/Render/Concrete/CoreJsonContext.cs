using System.Text.Json.Serialization;
using TMConcept.Core.Templates;

namespace TMConcept.Core.Render;

/// <summary>Source-generated JSON of Core's documents (no reflection; works with trimming).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RenderInput))]
[JsonSerializable(typeof(ChannelTemplate))]
[JsonSerializable(typeof(Models.StepOptions))]
[JsonSerializable(typeof(Models.ModelCatalogFile))]
[JsonSerializable(typeof(Videos.VideoOptions))]
[JsonSerializable(typeof(Styles.VideoStyle))]
[JsonSerializable(typeof(List<Styles.VideoStyle>))]
[JsonSerializable(typeof(List<Styles.SavedPalette>))]
[JsonSerializable(typeof(Concepts.FocusCatalogFile))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<Videos.AssetEntry>))]
[JsonSerializable(typeof(List<Videos.TimeNote>))]
[JsonSerializable(typeof(Music.MusicCatalogFile))]
[JsonSerializable(typeof(Videos.QualityReport))]
public sealed partial class CoreJsonContext : JsonSerializerContext;
