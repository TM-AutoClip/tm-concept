namespace TMConcept.Desktop.Updates;

/// <summary>
/// The last look for a new version (<c>update-check.json</c> in the data folder): when it was, GitHub's ETag (a repeated look that finds
/// nothing new does not count against the hourly limit), the release found, and until when GitHub asked not to be asked again (rate limit).
/// </summary>
public sealed class UpdateCheckState
{
    /// <summary>When GitHub last answered (UTC); null = never.</summary>
    public DateTimeOffset? CheckedAtUtc { get; set; }

    /// <summary>GitHub's ETag of that answer.</summary>
    public string? ETag { get; set; }

    /// <summary>The latest release in that answer.</summary>
    public GitHubRelease? Release { get; set; }

    /// <summary>No look before this time (GitHub's rate limit said so); null = none.</summary>
    public DateTimeOffset? NotBeforeUtc { get; set; }
}
