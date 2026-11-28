using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.DiskSpace;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.DiskSpace;

/// <inheritdoc cref="ILeftoverSweeper" />
public sealed class LeftoverSweeper(AppPaths paths, ILeftoverSweep sweep, IComponentWorkGate gate, IFolderDiscard discard, IActivityLog activity,
    Localizer text, ILogger<LeftoverSweeper> logger, Core.Channels.IChannelInspirer? channels = null) : ILeftoverSweeper
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
        var targets = await Task.Run(() => Targets(tools), cancellationToken).ConfigureAwait(false);
        long freed = 0;
        var kept = 0;
        foreach (var target in targets)
        {
            if (gate.Busy)
            {
                logger.LogInformation("Start-up sweep stopped: a component install began");
                break;
            }

            var before = await Task.Run(() => SizeOf(target), cancellationToken).ConfigureAwait(false);
            try
            {
                await discard.DiscardAsync(target, cancellationToken).ConfigureAwait(false);
                freed += before;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // A file in use (a virus scanner, an Explorer window): what could not go stays for the next start.
                freed += Math.Max(0, before - SizeOf(target));
                kept++;
                logger.LogWarning(LogEvents.OnScreen, ex, "Start-up sweep: {Folder} could not be deleted; it is tried again at the next start", Name(tools, target));
            }
        }

        logger.LogInformation("Start-up sweep: {Count} leftover(s), {Bytes} bytes freed, {Kept} kept for the next start", targets.Count - kept, freed, kept);
        if (kept > 0)
        {
            activity.Add(ActivityLevel.Warning, text.Format("storage.sweep.kept", kept));
        }

        if (freed > 0)
        {
            activity.Add(ActivityLevel.Info, text.Format("storage.sweep.done", Localization.ByteSizeText.Of(freed, text.Culture)));
        }

        return freed;
    }

    /// <summary>
    /// What to delete: the old components folders and the reinstall marker of a version up to 0.1.56 (whatever state they were left in: the
    /// components they held are installed again through the install journal), the staging folders, the engines' existing tmp folders and the
    /// closed looks of "Kanaldan esinlen".
    /// </summary>
    private List<string> Targets(string tools)
    {
        var targets = new List<string>();
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(tools));
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(tools));
        if (parent is not null && Directory.Exists(parent))
        {
            targets.AddRange(sweep.OldComponentFolders(tools, Folders(parent, name + ".*")));
            var marker = sweep.OldReinstallMarker(tools);
            if (File.Exists(marker))
            {
                targets.Add(marker);
            }
        }

        if (Directory.Exists(tools))
        {
            targets.AddRange(sweep.Stagings(tools, Stagings(tools)));
        }

        var engines = Path.Combine(tools, LeftoverSweep.EnginesFolder);
        if (Directory.Exists(engines))
        {
            targets.AddRange(sweep.EngineTemps(tools, Folders(engines, "*")).Where(Directory.Exists));
        }

        // 0.1.53: "Kanaldan esinlen" folders a closed app left (a channel photo; never one of a look that runs or waits for its preview).
        var channel = Path.Combine(paths.WorkDirectory, Core.Channels.ChannelLimits.Folder);
        if (Directory.Exists(channel))
        {
            targets.AddRange(sweep.ChannelTemps(paths.WorkDirectory, Folders(channel, "*"), channels?.ActiveFolders ?? []));
        }

        foreach (var target in targets)
        {
            logger.LogInformation("Start-up sweep: {Leftover} is a leftover of an earlier install; it is deleted permanently", Name(tools, target));
        }

        return targets;
    }

    /// <summary>
    /// The candidates for staging folders: the shared downloads folder and every <c>*.unpack</c> folder up to three levels inside
    /// <paramref name="tools"/> (a tool unpacks next to its version folder, <c>tools/&lt;name&gt;/&lt;version&gt;.unpack</c>; a voice next to its
    /// model folder). Deeper folders belong to the installed packages themselves.
    /// </summary>
    private List<string> Stagings(string tools)
    {
        var found = new List<string> { Path.Combine(tools, Core.Components.ComponentFolders.DownloadsFolder) };
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, AttributesToSkip = FileAttributes.ReparsePoint };
            found.AddRange(Directory.EnumerateDirectories(tools, "*" + LeftoverSweep.UnpackSuffix, options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Start-up sweep: the components folder could not be listed");
        }

        return found.Where(Directory.Exists).ToList();
    }

    private static long SizeOf(string path) => File.Exists(path) ? new FileInfo(path).Length : DiskSizes.Of(path);

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
