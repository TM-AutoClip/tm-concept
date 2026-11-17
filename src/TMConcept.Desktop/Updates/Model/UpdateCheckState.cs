namespace TMConcept.Desktop.Updates;

/// <summary>
/// The last look for a new version (<c>update-check.json</c> in the data folder): when it was, GitHub's ETag of the release list's first page
/// (a repeated look that finds nothing new does not count against the hourly limit), the published releases found, and until when GitHub
/// asked not to be asked again (rate limit).
/// </summary>
public sealed class UpdateCheckState
{
    /// <summary>When GitHub last answered (UTC); null = never.</summary>
    public DateTimeOffset? CheckedAtUtc { get; set; }

    /// <summary>GitHub's ETag of the first page of that answer (null when the list was read only in part).</summary>
    public string? ETag { get; set; }

    /// <summary>
    /// The published releases in that answer (drafts and pre-releases left out), newest version first; null = not looked at in this form yet
    /// (a file from 0.1.17 kept only the latest release: the next look asks GitHub at once).
    /// </summary>
    public List<GitHubRelease>? Releases { get; set; }

    /// <summary>No look before this time (GitHub's rate limit said so); null = none.</summary>
    public DateTimeOffset? NotBeforeUtc { get; set; }
}
