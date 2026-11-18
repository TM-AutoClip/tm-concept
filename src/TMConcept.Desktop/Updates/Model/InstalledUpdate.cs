namespace TMConcept.Desktop.Updates;

/// <summary>
/// A newer version for the installed edition: its version, the full package and the delta packages leading to it (each with the SHA-256
/// Velopack's release index gives; the app checks every one against the release notes before anything is downloaded).
/// </summary>
/// <param name="Version">The new version (<c>0.1.27</c>).</param>
/// <param name="Full">The full package of that version.</param>
/// <param name="Deltas">The delta packages from the installed version to it, oldest first (empty: the full package is downloaded).</param>
public sealed record InstalledUpdate(string Version, InstalledUpdateFile Full, IReadOnlyList<InstalledUpdateFile> Deltas)
{
    /// <summary>Velopack's own answer (kept for the download and the apply; null in tests).</summary>
    public object? Handle { get; init; }

    /// <summary>Every package that may be downloaded.</summary>
    public IReadOnlyList<InstalledUpdateFile> Files => [Full, .. Deltas];

    /// <summary>About how much is downloaded: the deltas when there are any, otherwise the full package.</summary>
    public long DownloadBytes => Deltas.Count > 0 ? Deltas.Sum(d => d.Size) : Full.Size;
}
