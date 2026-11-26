using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// Installs the installed edition from the portable program (Settings → General → Install TM Concept): downloads Setup of the running version's own release (on the project's release page, its SHA-256 from
/// the release notes and GitHub's digest; missing or different = refused), starts it and closes this app so the installed copy can open the
/// same data folder. The portable file stays where it is. Windows only: Linux keeps the portable archive.
/// </summary>
public sealed class EditionInstaller(ReleaseFeed feed, Engines.Tools.ToolDownloader downloader, SelfReplacer replacer, IAppEdition edition,
    ISetupLauncher launcher, IAppRestarter restarter, AppSession session, InstalledCopy installed, ILogger<EditionInstaller> logger)
{
    /// <summary>The running version (whose release's Setup is installed).</summary>
    public string Current { get; init; } = AppIdentity.ShortVersion;

    /// <summary>This computer's runtime id.</summary>
    public string Rid { get; init; } = ReleaseNotes.CurrentRid();

    /// <summary>Whether this copy can install the installed edition: the published portable program on Windows.</summary>
    public bool Available => edition.Kind == AppEditionKind.Portable && Rid.StartsWith("win-", StringComparison.Ordinal) && replacer.IsPackaged;

    /// <summary>The version of the installed edition already on this computer; null when it is not installed.</summary>
    public string? InstalledVersion => installed.Version();

    /// <summary>
    /// Downloads, checks and starts Setup, then closes the app. <see cref="AppUpdateException"/> ("not_installable": this copy cannot;
    /// "already_installed": the installed edition of this version or a newer one (or of an unknown one) is on this computer, and Setup would
    /// repair or downgrade it; "no_release" / "no_asset" / "no_hash": the release, its Setup or Setup's SHA-256 is missing; "not_writable": no
    /// folder to download it into; "busy": work runs) or
    /// <see cref="ToolInstallException"/> (the download) when nothing was started.
    /// </summary>
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        if (!Available)
        {
            throw new AppUpdateException("Only the portable Windows program installs TM Concept.") { Code = "not_installable" };
        }

        if (InstalledVersion is { } there && !(AppVersion.TryParse(there, out var installedVersion) && AppVersion.TryParse(Current, out var running)
            && running.IsNewerThan(installedVersion)))
        {
            logger.LogInformation("TM Concept {Installed} is already installed on this computer; Setup of {Version} is not run", there, Current);
            throw new AppUpdateException("TM Concept " + there + " is already installed.") { Code = "already_installed" };
        }

        if (session.WorkRunning)
        {
            throw new AppUpdateException("Work is running.") { Code = "busy" };
        }

        var releases = await feed.ReleasesAsync(cancellationToken).ConfigureAwait(false);
        var release = releases.FirstOrDefault(r => AppVersion.TryParse(r.Tag, out var v) && AppVersion.TryParse(Current, out var c) && v == c)
            ?? throw new AppUpdateException("The release of " + Current + " was not found.") { Code = "no_release" };
        var setup = ReleaseNotes.SetupFor(release, Rid);
        if (setup is null || !setup.DownloadUrl.StartsWith(AppUpdater.DownloadPrefix, StringComparison.Ordinal))
        {
            throw new AppUpdateException("The release of " + Current + " has no installer on the project's release page.") { Code = "no_asset" };
        }

        var hash = ReleaseNotes.Sha256For(release.Body, setup.Name);
        if (hash is null || (setup.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(digest["sha256:".Length..], hash, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AppUpdateException("The installer's SHA-256 is missing from the release notes or differs from GitHub's.") { Code = "no_hash" };
        }

        if (!replacer.CanWrite())
        {
            throw new AppUpdateException("The program's folder cannot be written.") { Code = "not_writable" };
        }

        logger.LogInformation("Installing TM Concept {Version}: downloading {File}", Current, setup.Name);
        var file = Path.Combine(replacer.Staging, setup.Name);
        var archive = new ToolArchive(ToolKind.AppUpdate, Current, Rid, new Uri(setup.DownloadUrl), hash, string.Empty, setup.Name, setup.Size);
        await downloader.DownloadAsync(archive, file, progress, cancellationToken).ConfigureAwait(false);
        if (session.WorkRunning)
        {
            throw new AppUpdateException("Work is running.") { Code = "busy" };
        }

        cancellationToken.ThrowIfCancellationRequested();
        launcher.Start(file);
        logger.LogInformation("Setup runs; this portable copy closes so the installed one can open the data folder");
        restarter.Close();
    }
}
