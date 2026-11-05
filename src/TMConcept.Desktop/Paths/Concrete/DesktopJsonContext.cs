using System.Text.Json.Serialization;

namespace TMConcept.Desktop.Paths;

/// <summary>Source-generated JSON of the desktop app's own files.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UiPreferences))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class DesktopJsonContext : JsonSerializerContext;
