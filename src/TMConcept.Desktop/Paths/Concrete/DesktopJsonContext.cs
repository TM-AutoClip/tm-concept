using System.Text.Json.Serialization;

namespace TMConcept.Desktop.Paths;

/// <summary>Source-generated JSON of the desktop app's own files.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UiPreferences))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<Core.Components.InstalledComponent>))]
[JsonSerializable(typeof(Updates.UpdateCheckState))]
[JsonSerializable(typeof(Updates.GitHubRelease))]
[JsonSerializable(typeof(List<Updates.GitHubRelease>))]
internal sealed partial class DesktopJsonContext : JsonSerializerContext;
