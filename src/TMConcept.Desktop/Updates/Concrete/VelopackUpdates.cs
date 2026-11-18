using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// <see cref="IInstalledUpdates"/> through Velopack's <see cref="UpdateManager"/> with the project's GitHub releases (no token, no pre-releases;
/// each release carries <c>releases.&lt;channel&gt;.json</c> listing its own packages). Only the installed edition uses it: Velopack refuses a
/// check from a program it did not install.
/// </summary>
public sealed class VelopackUpdates(IAppEdition edition, ILogger<VelopackUpdates> logger) : IInstalledUpdates
{
    private UpdateManager? _manager;

    /// <inheritdoc />
    public async Task<InstalledUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        var manager = Manager();
        cancellationToken.ThrowIfCancellationRequested();
        var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (info is null)
        {
            return null;
        }

        var deltas = info.DeltasToTarget.Select(File).ToList();
        logger.LogInformation("Installed edition: {Version} is available ({Deltas} delta package(s))", info.TargetFullRelease.Version, deltas.Count);
        return new InstalledUpdate(info.TargetFullRelease.Version.ToString(), File(info.TargetFullRelease), deltas) { Handle = info };
    }

    /// <inheritdoc />
    public Task DownloadAsync(InstalledUpdate update, Action<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return Manager().DownloadUpdatesAsync(Info(update), percent, cancellationToken);
    }

    /// <inheritdoc />
    public void ApplyAfterExit(InstalledUpdate update, IReadOnlyList<string> restartArguments)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(restartArguments);
        Manager().WaitExitThenApplyUpdates(Info(update).TargetFullRelease, silent: false, restart: true, [.. restartArguments]);
        logger.LogInformation("Installed edition: {Version} is applied once the app has closed", update.Version);
    }

    private static InstalledUpdateFile File(VelopackAsset asset) =>
        new(asset.FileName, asset.Version.ToString(), asset.SHA256 ?? string.Empty, asset.Size);

    private static UpdateInfo Info(InstalledUpdate update) =>
        update.Handle as UpdateInfo ?? throw new InvalidOperationException("The update was not found by Velopack.");

    private UpdateManager Manager()
    {
        if (edition.Kind != AppEditionKind.Installed)
        {
            throw new InvalidOperationException("Only the installed edition updates through Velopack.");
        }

        return _manager ??= new UpdateManager(new GithubSource(UpdateRules.Repository, accessToken: null, prerelease: false, downloader: null),
            new UpdateOptions());
    }
}
