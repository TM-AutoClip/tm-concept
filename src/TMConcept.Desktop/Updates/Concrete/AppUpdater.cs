using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// The app's own updates (the developer, 16 Nov 2026): looks for a newer release on GitHub (<see cref="ReleaseFeed"/>: at start at most once
/// a day when <see cref="UiPreferences.CheckForUpdates"/> is on, and on "Check now"), offers it with the notes of every version between the
/// running one and the newest (17 Nov 2026: no server, GitHub's release list; only the newest is downloaded), and only after
/// the user confirmed downloads the file for this computer (with progress, retries and resume, <see cref="Engines.Tools.ToolDownloader"/>),
/// checks it against the SHA-256 the release notes give (missing or different = refused), stops the idle speech worker, replaces the
/// program (<see cref="SelfReplacer"/>) and starts it again; the database chain runs at that next start. Nothing is downloaded without the
/// confirmation, and nothing is replaced while work runs.
/// <para>0.1.26, two editions (<see cref="IAppEdition"/>): the portable program works as above. The installed edition updates through
/// Velopack (<see cref="IInstalledUpdates"/>: small delta packages when the previous version is on disk); every package it would download
/// must have the same SHA-256 in its release's notes as in Velopack's index (and GitHub's digest), or the update is not offered for install.
/// The installed edition's apply ends every process of the install folder, so it runs only after this app has closed.</para>
/// </summary>
public sealed class AppUpdater(ReleaseFeed feed, SelfReplacer replacer, Engines.Tools.ToolDownloader downloader, UiPreferencesFile preferences,
    IAppRestarter restarter, AppSession session, Speech.Speech.SpeechWorkerClient speech, TimeProvider clock, IAppEdition edition,
    IInstalledUpdates installed, ILogger<AppUpdater> logger)
{
    /// <summary>The start of every release file address the app downloads (the project's own releases on GitHub).</summary>
    public const string DownloadPrefix = "https://github.com/TM-AutoClip/tm-concept/releases/download/";

    /// <summary>The running version.</summary>
    public string Current { get; init; } = AppIdentity.ShortVersion;

    /// <summary>This computer's runtime id (which release file fits).</summary>
    public string Rid { get; init; } = ReleaseNotes.CurrentRid();

    /// <summary>
    /// The newer release as an offer, or null when the running version is the newest (or nothing is known). <paramref name="force"/>: ask
    /// GitHub now (Check now), otherwise at most once a day.
    /// </summary>
    public async Task<UpdateOffer?> CheckAsync(bool force, CancellationToken cancellationToken)
    {
        var releases = await feed.ReleasesAsync(force, cancellationToken).ConfigureAwait(false);
        var release = releases.FirstOrDefault();
        if (release is null || !AppVersion.TryParse(release.Tag, out var latest))
        {
            return null;
        }

        if (!AppVersion.TryParse(Current, out var current) || !latest.IsNewerThan(current))
        {
            logger.LogInformation("Update check: {Current} is up to date (latest {Latest})", Current, latest);
            return null;
        }

        // Every published version between the running one and the newest, oldest first: the window shows all their notes.
        var versions = releases
            .Select(r => (Release: r, Ok: AppVersion.TryParse(r.Tag, out var version), Version: version))
            .Where(r => r.Ok && r.Version.IsNewerThan(current))
            .OrderBy(r => r.Version)
            .Select(r => new VersionNotes(r.Version, r.Release.Body ?? string.Empty, r.Release.PublishedAt))
            .ToList();
        var page = Uri.TryCreate(release.Page, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps ? address : UpdateRules.ReleasesPage;
        if (edition.Kind == AppEditionKind.Installed)
        {
            return await InstalledOfferAsync(releases, release, latest, page, versions, cancellationToken).ConfigureAwait(false);
        }

        var asset = Rid.Length == 0 ? null : ReleaseNotes.AssetFor(release, Rid);
        if (asset is not null && !asset.DownloadUrl.StartsWith(DownloadPrefix, StringComparison.Ordinal))
        {
            logger.LogWarning("Update check: the release file {File} is not on the project's release page; it is not offered for install", asset.Name);
            asset = null;
        }

        var hash = asset is null ? null : Verified(release, asset);
        var problem = !replacer.IsPackaged ? "not_packaged" : asset is null ? "no_asset" : hash is null ? "no_hash" : string.Empty;
        logger.LogInformation("Update check: {Latest} is newer than {Current} ({Count} newer version(s); file {File}; {State})", latest, Current,
            versions.Count, asset?.Name ?? "none", problem.Length == 0 ? "can be installed" : problem);
        return new UpdateOffer(latest, Current, release.Body ?? string.Empty, page, asset, hash, problem) { Versions = versions };
    }

    /// <summary>
    /// The start's look: nothing when it is turned off; otherwise the newer release, offered once a day at most (the same version is not
    /// shown again within a day after "Later").
    /// </summary>
    public async Task<UpdateOffer?> CheckAtStartAsync(CancellationToken cancellationToken)
    {
        var settings = preferences.Read();
        if (!settings.CheckForUpdates)
        {
            logger.LogInformation("Update check at start is turned off");
            return null;
        }

        var offer = await CheckAsync(force: false, cancellationToken).ConfigureAwait(false);
        if (offer is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        if (settings.UpdateOfferedVersion == offer.Version.ToString() && settings.UpdateOfferedAtUtc is { } at && now - at < UpdateRules.CheckEvery && now >= at)
        {
            return null;
        }

        try
        {
            preferences.Change(p =>
            {
                p.UpdateOfferedVersion = offer.Version.ToString();
                p.UpdateOfferedAtUtc = now;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The offered version could not be kept; it may be offered again at the next start");
        }

        return offer;
    }

    /// <summary>
    /// Installs a confirmed offer: downloads its file (progress, retries, resume), checks the SHA-256, unpacks it, stops the idle speech
    /// worker, replaces the program and starts it again (this app closes). <see cref="AppUpdateException"/> or
    /// <see cref="ToolInstallException"/> when nothing was replaced.
    /// </summary>
    public async Task InstallAsync(UpdateOffer offer, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (!offer.CanInstall || offer.Asset is null || offer.Sha256 is null)
        {
            throw new AppUpdateException("This release cannot be installed by the app: " + offer.Problem) { Code = "not_installable" };
        }

        if (session.WorkRunning)
        {
            throw new AppUpdateException("Work is running.") { Code = "busy" };
        }

        if (offer.Installed is { } update)
        {
            await InstallInstalledAsync(offer, update, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!replacer.CanWrite())
        {
            throw new AppUpdateException("The program's folder cannot be written.") { Code = "not_writable" };
        }

        logger.LogInformation("Update to {Version}: downloading {File}", offer.Version, offer.Asset.Name);
        var file = Path.Combine(replacer.Staging, offer.Asset.Name);
        var archive = new ToolArchive(ToolKind.AppUpdate, offer.Version.ToString(), Rid, new Uri(offer.Asset.DownloadUrl), offer.Sha256, string.Empty,
            offer.Asset.Name, offer.Asset.Size);
        await downloader.DownloadAsync(archive, file, progress, cancellationToken).ConfigureAwait(false);
        var program = await Task.Run(() => replacer.Unpack(file), cancellationToken).ConfigureAwait(false);

        // The speech worker is this same program started again: it ends before its file is replaced (one that is speaking is waited for by
        // the user, not killed).
        await StopSpeechWorkerAsync().ConfigureAwait(false);

        // The last moment a cancel (the window closed) still keeps the running version: after this the program file is the new one.
        cancellationToken.ThrowIfCancellationRequested();
        var started = replacer.Replace(program);
        logger.LogInformation("Update to {Version} installed; restarting", offer.Version);
        try
        {
            restarter.Restart(started);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Installed, but the new program did not start (a virus scanner holding it, for example): this one keeps running from the old
            // file until it is closed; the next start is the new version.
            logger.LogError(ex, "Update to {Version} installed, but the new version could not be started", offer.Version);
            throw new AppUpdateException("The update was installed, but the new version could not be started.", ex) { Code = "restart" };
        }
    }

    /// <summary>
    /// The installed edition's offer: Velopack's newer version must be the newest release, and each package it may download must carry the
    /// same SHA-256 in its release's notes (and GitHub's digest) as in Velopack's index.
    /// </summary>
    private async Task<UpdateOffer> InstalledOfferAsync(IReadOnlyList<GitHubRelease> releases, GitHubRelease release, AppVersion latest, Uri page,
        List<VersionNotes> versions, CancellationToken cancellationToken)
    {
        InstalledUpdate? update;
        try
        {
            update = await installed.CheckAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Velopack's own reading of the releases failed (network, a release without its index): the release page is offered instead.
            logger.LogWarning(ex, "Update check (installed edition): the update packages could not be looked at");
            update = null;
        }

        var problem = string.Empty;
        if (update is null || !AppVersion.TryParse(update.Version, out var target) || target != latest)
        {
            problem = "no_asset";
        }
        else
        {
            foreach (var file in update.Files)
            {
                var carrier = releases.FirstOrDefault(r => AppVersion.TryParse(r.Tag, out var v) && AppVersion.TryParse(file.Version, out var f) && v == f);
                var asset = carrier?.Assets.FirstOrDefault(a => string.Equals(a.Name, file.FileName, StringComparison.Ordinal));
                var hash = carrier is null || asset is null ? null : Verified(carrier, asset);
                if (hash is null || !string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Update check (installed edition): {File} has no matching SHA-256 in the notes of {Version}; it is not offered for install",
                        file.FileName, file.Version);
                    problem = "no_hash";
                    break;
                }
            }
        }

        var shown = update is null ? null : new GitHubAsset { Name = update.Full.FileName, Size = update.DownloadBytes };
        logger.LogInformation("Update check (installed edition): {Latest} is newer than {Current} ({Count} newer version(s); {Packages}; {State})", latest,
            Current, versions.Count, update is null ? "no packages" : string.Join(", ", update.Files.Select(f => f.FileName)),
            problem.Length == 0 ? "can be installed" : problem);
        return new UpdateOffer(latest, Current, release.Body ?? string.Empty, page, shown, update?.Full.Sha256, problem)
        {
            Versions = versions,
            Installed = problem.Length == 0 ? update : null,
        };
    }

    /// <summary>Downloads the installed edition's packages, then hands the apply to Velopack's updater and closes the app.</summary>
    private async Task InstallInstalledAsync(UpdateOffer offer, InstalledUpdate update, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        logger.LogInformation("Update to {Version} (installed edition): downloading {Files}", offer.Version, string.Join(", ", update.Files.Select(f => f.FileName)));
        await installed.DownloadAsync(update, p => progress?.Report(new ToolProgress(p / 100.0, string.Empty)), cancellationToken).ConfigureAwait(false);
        await StopSpeechWorkerAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var arguments = new List<string>();
        if (StartOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToList()).DataDirectory is { } data)
        {
            arguments.AddRange(["--data-dir", data]);
        }

        try
        {
            installed.ApplyAfterExit(update, arguments);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "Update to {Version} was downloaded, but Velopack's updater could not be started", offer.Version);
            throw new AppUpdateException("The update was downloaded, but the updater could not be started.", ex) { Code = "restart" };
        }

        logger.LogInformation("Update to {Version} (installed edition): closing so it can be applied", offer.Version);
        restarter.Close();
    }

    private async Task StopSpeechWorkerAsync()
    {
        if (session.WorkRunning || !await Task.Run(speech.StopIfIdle, CancellationToken.None).ConfigureAwait(false))
        {
            throw new AppUpdateException("Work is running.") { Code = "busy" };
        }
    }

    /// <summary>The SHA-256 of <paramref name="asset"/> from <paramref name="release"/>'s notes, when it is on the project's release page and GitHub's digest agrees.</summary>
    private string? Verified(GitHubRelease release, GitHubAsset asset)
    {
        if (!asset.DownloadUrl.StartsWith(DownloadPrefix, StringComparison.Ordinal))
        {
            logger.LogWarning("Update check: the release file {File} is not on the project's release page; it is not offered for install", asset.Name);
            return null;
        }

        var hash = ReleaseNotes.Sha256For(release.Body, asset.Name);
        if (hash is not null && asset.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(digest["sha256:".Length..], hash, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Update check: GitHub's digest of {File} differs from the SHA-256 in the release notes; it is not offered for install", asset.Name);
            return null;
        }

        return hash;
    }
}
