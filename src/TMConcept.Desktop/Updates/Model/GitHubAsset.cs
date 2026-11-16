using System.Text.Json.Serialization;

namespace TMConcept.Desktop.Updates;

/// <summary>One file of a GitHub release (the fields the updater reads).</summary>
public sealed class GitHubAsset
{
    /// <summary>File name (<c>TMConcept-0.1.18-win-x64.exe</c>).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The download address (github.com, redirected to GitHub's file servers).</summary>
    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Size in bytes.</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>GitHub's own digest (<c>sha256:…</c>) when it gives one; it must agree with the release notes.</summary>
    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}
