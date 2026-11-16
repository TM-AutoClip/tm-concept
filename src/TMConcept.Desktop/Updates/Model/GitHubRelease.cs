using System.Text.Json.Serialization;

namespace TMConcept.Desktop.Updates;

/// <summary>A GitHub release (the fields the updater reads).</summary>
public sealed class GitHubRelease
{
    /// <summary>The tag (<c>v0.1.18</c>).</summary>
    [JsonPropertyName("tag_name")]
    public string Tag { get; set; } = string.Empty;

    /// <summary>The release title.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The release notes (Markdown; ours end with a SHA-256 block of every file).</summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>A draft (never offered).</summary>
    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    /// <summary>A pre-release (never offered).</summary>
    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    /// <summary>The release's page.</summary>
    [JsonPropertyName("html_url")]
    public string? Page { get; set; }

    /// <summary>When it was published.</summary>
    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Its files.</summary>
    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];
}
