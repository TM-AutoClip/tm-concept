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
/// changed; nothing else is touched. <see cref="ReinstallAsync"/> first ends the idle speech workers (they keep the speech libraries and the
/// Python voice's environment open for a minute after the last scene), renames the whole components folder (<c>&lt;data&gt;/tools</c>) aside in one step (a file in use makes the
/// rename fail and nothing changes; a half-moved folder cannot happen), moves that folder to the recycle bin (when that fails, a permanent
/// delete only after its own confirmation that says so; declined, the folder is put back and the message says whether it is still whole),
/// then installs again what was there. The database, the profiles, the concepts and the videos are outside that folder and never touched.
/// </summary>
public sealed class ComponentReinstaller(ComponentPlanner planner, ComponentInstallFlow flow, IRecycleBin bin, IConfirmDialog confirm, AppPaths paths,
    Speech.Speech.SpeechWorkerClient speech, Speech.Python.PythonWorkerClient python, IActivityLog activity, Localizer text, TimeProvider clock, ILogger<ComponentReinstaller> logger)
{
    /// <summary>The text of what an install confirmation says a cancel means here.</summary>
    public const string PurposeKey = "install.purpose.update";

    /// <summary>The start of the name the components folder gets while it is being removed (<c>tools.old-20261116-120000</c>).</summary>
    public const string AsidePrefix = ".old-";

    /// <summary>
    /// Where the components live, as the user knows the folder: <c>%LOCALAPPDATA%\TM Concept\tools</c> on Windows,
    /// <c>~/.local/share/tm-concept/tools</c> on Linux (a portable data folder as it is).
    /// </summary>
    public string Location() => Location(paths.ToolsDirectory);

    /// <summary>A folder as the user knows it (<see cref="Location()"/>).</summary>
    public static string Location(string tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
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
    /// Sets the components folder aside, moves it to the recycle bin and installs again what was installed. When the recycle bin cannot take
    /// it, a permanent delete is asked for (declined = the folder is put back). The outcome says what happened (not ready = kept, partly
    /// moved, failed or declined).
    /// </summary>
    public async Task<InstallOutcome> ReinstallAsync(CancellationToken cancellationToken)
    {
        var codes = await InstalledAsync(cancellationToken).ConfigureAwait(true);
        var tools = paths.ToolsDirectory;
        logger.LogInformation("Components reinstall: {Count} were installed ({Codes})", codes.Count, string.Join(", ", codes));
        if (Directory.Exists(tools))
        {
            if (!await Task.Run(() => speech.StopIfIdle() & python.StopIfIdle(), CancellationToken.None).ConfigureAwait(true))
            {
                logger.LogInformation("Components reinstall: the speech worker is speaking; nothing was changed");
                return new InstallOutcome(false, text["components.reinstall.busy"]);
            }

            var aside = Path.TrimEndingDirectorySeparator(tools) + AsidePrefix
                + clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var files = Count(tools);
            try
            {
                Directory.Move(tools, aside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A component in use (a program from the folder still running, an open file): the rename is all or nothing.
                logger.LogWarning(LogEvents.OnScreen, ex, "The components folder could not be set aside (in use); nothing was changed");
                var line = text.Format("components.reinstall.in_use", ex.Message);
                activity.Add(ActivityLevel.Warning, line);
                return new InstallOutcome(false, line);
            }

            try
            {
                await bin.MoveAsync(aside, cancellationToken).ConfigureAwait(true);
                logger.LogInformation("The components folder was moved to the recycle bin");
                activity.Add(ActivityLevel.Info, text["components.reinstall.moved"]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException
                or OperationCanceledException)
            {
                logger.LogWarning(ex, "The components folder could not be moved to the recycle bin");
                if (await DeleteAsync(tools, aside, files, ex.Message).ConfigureAwait(true) is { } stopped)
                {
                    return stopped;
                }
            }
        }

        await planner.RefreshAllAsync(cancellationToken).ConfigureAwait(true);
        return codes.Count == 0 ? new InstallOutcome(true, string.Empty) : await flow.EnsureAsync(codes, PurposeKey, null, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// The permanent delete of the set-aside folder after its own confirmation; null when it is gone, otherwise why the reinstall stopped.
    /// Declined: what is left goes back to its place, and the message says whether all of it was still there.
    /// </summary>
    private async Task<InstallOutcome?> DeleteAsync(string tools, string aside, int files, string reason)
    {
        var answer = await confirm.AskAsync(text["components.reinstall.bin_failed.title"], text.Format("components.reinstall.bin_failed", reason, Location(tools)),
            text["components.reinstall.delete"], danger: true).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return PutBack(tools, aside, files);
        }

        try
        {
            if (Directory.Exists(aside))
            {
                await Task.Run(() => Directory.Delete(aside, recursive: true)).ConfigureAwait(true);
            }

            logger.LogInformation("The components folder was deleted permanently");
            activity.Add(ActivityLevel.Info, text["components.reinstall.deleted"]);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(LogEvents.OnScreen, ex, "The components folder could not be deleted");
            var line = text.Format("components.reinstall.failed", ex.Message);
            activity.Add(ActivityLevel.Error, line);
            return new InstallOutcome(false, line);
        }
    }

    /// <summary>
    /// Puts the set-aside folder back after a declined delete. "Nothing was changed" only when every file is back; a recycle bin that took a
    /// part says so (the missing components are installed again when they are next needed).
    /// </summary>
    private InstallOutcome PutBack(string tools, string aside, int files)
    {
        try
        {
            if (Directory.Exists(aside) && !Directory.Exists(tools))
            {
                Directory.Move(aside, tools);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(LogEvents.OnScreen, ex, "The components folder could not be put back");
            var line = text.Format("components.reinstall.not_back", Location(aside));
            activity.Add(ActivityLevel.Error, line);
            return new InstallOutcome(false, line);
        }

        var left = Directory.Exists(tools) ? Count(tools) : 0;
        if (left == files)
        {
            logger.LogInformation("The permanent delete of the components folder was declined; nothing changed");
            return new InstallOutcome(false, text["components.reinstall.kept"]);
        }

        logger.LogWarning(LogEvents.OnScreen, "The permanent delete was declined after a partial move: {Left} of {Files} files are back", left, files);
        var partial = text.Format("components.reinstall.partial", Location(tools));
        activity.Add(ActivityLevel.Warning, partial);
        return new InstallOutcome(false, partial);
    }

    private static int Count(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count();
}
