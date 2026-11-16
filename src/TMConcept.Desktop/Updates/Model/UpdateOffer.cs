namespace TMConcept.Desktop.Updates;

/// <summary>
/// A newer version found on GitHub: its version, the running one, its notes and page, the file for this computer and that file's SHA-256 from
/// the release notes. <see cref="Problem"/> says why the app cannot install it itself (empty = it can): "not_packaged" (this is not the
/// published single-file program, for example a build from source), "no_asset" (no file for this system), "no_hash" (the notes name no
/// SHA-256 for the file, or two different ones, or GitHub's own digest disagrees), "not_writable" (the program's folder cannot be written).
/// </summary>
public sealed record UpdateOffer(AppVersion Version, string Current, string Notes, Uri Page, GitHubAsset? Asset, string? Sha256, string Problem)
{
    /// <summary>Whether the app can download and install it itself.</summary>
    public bool CanInstall => Problem.Length == 0;
}
