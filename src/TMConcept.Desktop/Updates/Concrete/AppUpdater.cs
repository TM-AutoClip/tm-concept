using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// The app's own updates (the developer, 16 Nov 2026): looks for a newer release on GitHub (<see cref="ReleaseFeed"/>: at start at most once
/// a day when <see cref="UiPreferences.CheckForUpdates"/> is on, and on "Check now"), offers it with its version and notes, and only after
/// the user confirmed downloads the file for this computer (with progress, retries and resume, <see cref="Engines.Tools.ToolDownloader"/>),
/// checks it against the SHA-256 the release notes give (missing or different = refused), stops the idle speech worker, replaces the
/// program (<see cref="SelfReplacer"/>) and starts it again; the database chain runs at that next start. Nothing is downloaded without the
/// confirmation, and nothing is replaced while work runs.
/// </summary>
public sealed class AppUpdater(ReleaseFeed feed, SelfReplacer replacer, Engines.Tools.ToolDownloader downloader, UiPreferencesFile preferences,
    IAppRestarter restarter, AppSession session, Speech.Speech.SpeechWorkerClient speech, TimeProvider clock, ILogger<AppUpdater> logger)
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
        var release = await feed.LatestAsync(force, cancellationToken).ConfigureAwait(false);
        if (release is null || !AppVersion.TryParse(release.Tag, out var latest))
        {
            return null;
        }

        if (!AppVersion.TryParse(Current, out var current) || !latest.IsNewerThan(current))
        {
            logger.LogInformation("Update check: {Current} is up to date (latest {Latest})", Current, latest);
            return null;
        }

        var asset = Rid.Length == 0 ? null : ReleaseNotes.AssetFor(release, Rid);
        if (asset is not null && !asset.DownloadUrl.StartsWith(DownloadPrefix, StringComparison.Ordinal))
        {
            logger.LogWarning("Update check: the release file {File} is not on the project's release page; it is not offered for install", asset.Name);
            asset = null;
        }

        var hash = asset is null ? null : ReleaseNotes.Sha256For(release.Body, asset.Name);
        if (hash is not null && asset!.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(digest["sha256:".Length..], hash, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Update check: GitHub's digest of {File} differs from the SHA-256 in the release notes; it is not offered for install", asset.Name);
            hash = null;
        }

        var problem = !replacer.IsPackaged ? "not_packaged" : asset is null ? "no_asset" : hash is null ? "no_hash" : string.Empty;
        var page = Uri.TryCreate(release.Page, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps ? address : UpdateRules.ReleasesPage;
        logger.LogInformation("Update check: {Latest} is newer than {Current} (file {File}; {State})", latest, Current, asset?.Name ?? "none",
            problem.Length == 0 ? "can be installed" : problem);
        return new UpdateOffer(latest, Current, release.Body ?? string.Empty, page, asset, hash, problem);
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
        if (session.WorkRunning || !await Task.Run(speech.StopIfIdle, CancellationToken.None).ConfigureAwait(false))
        {
            throw new AppUpdateException("Work is running.") { Code = "busy" };
        }

        replacer.Replace(program);
        logger.LogInformation("Update to {Version} installed; restarting", offer.Version);
        restarter.Restart(replacer.ProgramPath);
    }
}
