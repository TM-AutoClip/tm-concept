namespace TMConcept.Desktop.Updates;

/// <summary>
/// A newer version found on GitHub: its version, the running one, its notes and page, the file for this computer and that file's SHA-256 from
/// the release notes, and every published version between the running one and it (<see cref="Versions"/>, oldest first, the newest last;
/// only the newest is downloaded: each program carries the whole database upgrade chain, so skipped versions are safe).
/// <see cref="Problem"/> says why the app cannot install it itself (empty = it can): "not_packaged" (this is not the published single-file
/// program, for example a build from source), "no_asset" (no file for this system), "no_hash" (the notes name no SHA-256 for the file, or
/// two different ones, or GitHub's own digest disagrees), "not_writable" (the program's folder cannot be written).
/// </summary>
public sealed record UpdateOffer(AppVersion Version, string Current, string Notes, Uri Page, GitHubAsset? Asset, string? Sha256, string Problem)
{
    /// <summary>Every newer version, oldest first (the newest, <see cref="Version"/>, is the last); just the newest when nothing else is known.</summary>
    public IReadOnlyList<VersionNotes> Versions { get; init; } = [];

    /// <summary>The installed edition's packages (Velopack), checked against the notes; null for the portable program or when not installable.</summary>
    public InstalledUpdate? Installed { get; init; }

    /// <summary>Whether the app can download and install it itself.</summary>
    public bool CanInstall => Problem.Length == 0;
}
