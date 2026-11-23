using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.DiskSpace;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.DiskSpace;

/// <inheritdoc cref="ILeftoverSweeper" />
public sealed class LeftoverSweeper(AppPaths paths, ILeftoverSweep sweep, IComponentWorkGate gate, IFolderDiscard discard, IActivityLog activity,
    Localizer text, ILogger<LeftoverSweeper> logger) : ILeftoverSweeper
{
    /// <inheritdoc />
    public async Task<long> SweepAsync(CancellationToken cancellationToken)
    {
        if (gate.Busy)
        {
            logger.LogInformation("Start-up sweep skipped: a component install runs");
            return 0;
        }

        var tools = paths.ToolsDirectory;
        var interrupted = gate.Interrupted;
        if (interrupted)
        {
            // The previous run ended in the middle of a reinstall: an old folder may hold the components that worked. It stays for this run
            // (the storage page lists it); the marker goes, so the next start sweeps it.
            logger.LogWarning(LogEvents.OnScreen, "A components reinstall was cut short in the previous run; its old folders stay until the next start");
            activity.Add(ActivityLevel.Warning, text.Format("storage.sweep.interrupted", ComponentReinstaller.Location(tools)));
            gate.ForgetInterrupted();
        }

        var targets = await Task.Run(() => Targets(tools, interrupted), cancellationToken).ConfigureAwait(false);
        long freed = 0;
        var kept = 0;
        foreach (var folder in targets)
        {
            if (gate.Busy)
            {
                logger.LogInformation("Start-up sweep stopped: a component install began");
                break;
            }

            var before = await Task.Run(() => DiskSizes.Of(folder), cancellationToken).ConfigureAwait(false);
            try
            {
                await discard.DiscardAsync(folder, cancellationToken).ConfigureAwait(false);
                freed += before;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // A file in use (a virus scanner, an Explorer window): what could not go stays for the next start.
                freed += Math.Max(0, before - DiskSizes.Of(folder));
                kept++;
                logger.LogWarning(LogEvents.OnScreen, ex, "Start-up sweep: {Folder} could not be deleted; it is tried again at the next start", Name(tools, folder));
            }
        }

        logger.LogInformation("Start-up sweep: {Count} folder(s), {Bytes} bytes freed, {Kept} kept for the next start", targets.Count - kept, freed, kept);
        if (kept > 0)
        {
            activity.Add(ActivityLevel.Warning, text.Format("storage.sweep.kept", kept));
        }

        if (freed > 0)
        {
            activity.Add(ActivityLevel.Info, text.Format("storage.sweep.done", ViewModels.StorageSettingsViewModel.Size(freed, text.Culture)));
        }

        return freed;
    }

    /// <summary>The folders to delete: the old components folders (not after a cut-short reinstall) and the engines' existing tmp folders.</summary>
    private List<string> Targets(string tools, bool interrupted)
    {
        var targets = new List<string>();
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(tools));
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(tools));
        if (!interrupted && parent is not null && Directory.Exists(parent))
        {
            var present = Directory.Exists(tools);
            var old = sweep.OldComponentFolders(tools, Folders(parent, name + ".*"), gate.Reinstalling, present);
            if (!present)
            {
                logger.LogWarning(LogEvents.OnScreen, "Start-up sweep: the components folder is not there; the old components folders next to it stay");
            }

            targets.AddRange(old);
        }

        var engines = Path.Combine(tools, LeftoverSweep.EnginesFolder);
        if (Directory.Exists(engines))
        {
            targets.AddRange(sweep.EngineTemps(tools, Folders(engines, "*")).Where(Directory.Exists));
        }

        return targets;
    }

    private List<string> Folders(string folder, string pattern)
    {
        try
        {
            return Directory.EnumerateDirectories(folder, pattern).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Start-up sweep: a folder could not be listed");
            return [];
        }
    }

    private static string Name(string tools, string folder)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(tools));
        return parent is null ? Path.GetFileName(folder) : Path.GetRelativePath(parent, folder);
    }
}
