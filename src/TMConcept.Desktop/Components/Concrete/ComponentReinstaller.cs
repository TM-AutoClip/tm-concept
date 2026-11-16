using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Components;

/// <summary>
/// The components after a new app version, or on request (General settings): <see cref="UpdateAsync"/> installs again, through the one
/// install flow (<see cref="ComponentInstallFlow"/>: one confirmation, the install window), what was installed before and is now missing or
/// changed; nothing else is touched. <see cref="ReinstallAsync"/> first moves the whole components folder (<c>&lt;data&gt;/tools</c>) to the
/// recycle bin (when that fails, a permanent delete only after its own confirmation that says so), then installs again what was there.
/// The database, the profiles, the concepts and the videos are outside that folder and never touched.
/// </summary>
public sealed class ComponentReinstaller(ComponentPlanner planner, ComponentInstallFlow flow, IRecycleBin bin, IConfirmDialog confirm, AppPaths paths,
    IActivityLog activity, Localizer text, ILogger<ComponentReinstaller> logger)
{
    /// <summary>The text of what an install confirmation says a cancel means here.</summary>
    public const string PurposeKey = "install.purpose.update";

    /// <summary>
    /// Where the components live, as the user knows the folder: <c>%LOCALAPPDATA%\TM Concept\tools</c> on Windows,
    /// <c>~/.local/share/tm-concept/tools</c> on Linux (a portable data folder as it is).
    /// </summary>
    public string Location()
    {
        var tools = paths.ToolsDirectory;
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (local.Length > 0 && tools.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return "%LOCALAPPDATA%" + tools[local.Length..];
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && tools.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "~" + tools[home.Length..] : tools;
    }

    /// <summary>The codes of the components an install left on disk (this version's or an earlier one's), after looking at the disk again.</summary>
    public async Task<IReadOnlyList<string>> InstalledAsync(CancellationToken cancellationToken)
    {
        await planner.RefreshAllAsync(cancellationToken).ConfigureAwait(true);
        return planner.All.Where(c => c.WasInstalled).Select(c => c.Code).ToList();
    }

    /// <summary>Installs again what was installed before and is now missing or changed (one confirmation lists the downloads).</summary>
    public async Task<InstallOutcome> UpdateAsync(CancellationToken cancellationToken)
    {
        var codes = await InstalledAsync(cancellationToken).ConfigureAwait(true);
        logger.LogInformation("Components update: {Count} were installed before ({Codes})", codes.Count, string.Join(", ", codes));
        return codes.Count == 0 ? new InstallOutcome(true, string.Empty) : await flow.EnsureAsync(codes, PurposeKey, null, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Moves the components folder to the recycle bin and installs again what was installed. When the recycle bin cannot take it, a
    /// permanent delete is asked for (declined = nothing changed). The outcome says what happened (not ready = kept, failed or declined).
    /// </summary>
    public async Task<InstallOutcome> ReinstallAsync(CancellationToken cancellationToken)
    {
        var codes = await InstalledAsync(cancellationToken).ConfigureAwait(true);
        var tools = paths.ToolsDirectory;
        logger.LogInformation("Components reinstall: {Count} were installed ({Codes})", codes.Count, string.Join(", ", codes));
        if (Directory.Exists(tools))
        {
            try
            {
                await bin.MoveAsync(tools, cancellationToken).ConfigureAwait(true);
                logger.LogInformation("The components folder was moved to the recycle bin");
                activity.Add(ActivityLevel.Info, text["components.reinstall.moved"]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException
                or OperationCanceledException)
            {
                logger.LogWarning(ex, "The components folder could not be moved to the recycle bin");
                if (await DeleteAsync(tools, ex.Message).ConfigureAwait(true) is { } stopped)
                {
                    return stopped;
                }
            }
        }

        await planner.RefreshAllAsync(cancellationToken).ConfigureAwait(true);
        return codes.Count == 0 ? new InstallOutcome(true, string.Empty) : await flow.EnsureAsync(codes, PurposeKey, null, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>The permanent delete after its own confirmation; null when the folder is gone, otherwise why the reinstall stopped.</summary>
    private async Task<InstallOutcome?> DeleteAsync(string tools, string reason)
    {
        var answer = await confirm.AskAsync(text["components.reinstall.bin_failed.title"], text.Format("components.reinstall.bin_failed", reason, Location()),
            text["components.reinstall.delete"], danger: true).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            logger.LogInformation("The permanent delete of the components folder was declined; nothing changed");
            return new InstallOutcome(false, text["components.reinstall.kept"]);
        }

        try
        {
            await Task.Run(() => Directory.Delete(tools, recursive: true)).ConfigureAwait(true);
            logger.LogInformation("The components folder was deleted permanently");
            activity.Add(ActivityLevel.Info, text["components.reinstall.deleted"]);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "The components folder could not be deleted");
            var line = text.Format("components.reinstall.failed", ex.Message);
            activity.Add(ActivityLevel.Error, line);
            return new InstallOutcome(false, line);
        }
    }
}
