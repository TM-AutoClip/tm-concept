using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Components;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Components;

/// <summary>
/// The components after a new app version, or on request (General settings). What "was installed" comes from the install record
/// (<see cref="IInstallRecord"/>, kept outside the components folder, with the app version of each install) and from the folders that hold
/// an install; an empty folder or a partial download does not count.
/// <list type="bullet">
/// <item><see cref="UpdateAsync"/> installs again, through the one install flow (<see cref="ComponentInstallFlow"/>: one confirmation, the
/// install window), what was installed before and is now missing or changed; nothing else is touched.</item>
/// <item><see cref="ReinstallAsync"/> ends the idle speech workers (they keep the speech libraries and the Python voice's environment open
/// for a minute after the last scene), renames the whole components folder (<c>&lt;data&gt;/tools</c>) aside in one step (a file in use makes
/// the rename fail and nothing changes), installs again what was there into a new, empty folder, and only when that ended ready deletes the
/// old folder for good (0.1.28: no recycle bin, which frees nothing for a folder of gigabytes; the confirmation said so with the size; a
/// failure is shown with where the folder stays). A declined, cancelled or failed install puts the old folder back in its place (0.1.16 removed it first, so a declined
/// download left nothing installed and the next start did not ask again).</item>
/// </list>
/// The database, the profiles, the concepts and the videos are outside that folder and never touched.
/// </summary>
public sealed class ComponentReinstaller(ComponentPlanner planner, ComponentInstallFlow flow, Core.DiskSpace.IFolderDiscard discard, AppPaths paths,
    Speech.Speech.SpeechWorkerClient speech, Speech.Python.PythonWorkerClient python, IActivityLog activity, Localizer text, TimeProvider clock, IComponentWorkGate gate,
    ILogger<ComponentReinstaller> logger)
{
    /// <summary>The text of what an install confirmation says a cancel means here.</summary>
    public const string PurposeKey = "install.purpose.update";

    /// <summary>The start of the name the old components folder gets while the new one is installed (<c>tools.old-20261116-120000</c>).</summary>
    public const string AsidePrefix = Core.DiskSpace.LeftoverSweep.OldInfix;

    /// <summary>The start of the name a new components folder gets when its install did not finish and the old one goes back.</summary>
    public const string FailedPrefix = Core.DiskSpace.LeftoverSweep.FailedInfix;

    /// <summary>
    /// Where the components live, as the user knows the folder: <c>%LOCALAPPDATA%\TM Concept\tools</c> on Windows,
    /// <c>~/.local/share/tm-concept/tools</c> on Linux (a portable data folder as it is).
    /// </summary>
    public string Location() => Location(paths.ToolsDirectory);

    /// <summary>The components folder (its size goes into the reinstall confirmation).</summary>
    public string Folder => paths.ToolsDirectory;

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

    /// <summary>
    /// The codes of the components that were installed (this version's or an earlier one's): the install record's and those on disk, after
    /// looking at the disk again.
    /// </summary>
    public Task<IReadOnlyList<string>> InstalledAsync(CancellationToken cancellationToken) => planner.InstalledAsync(cancellationToken);

    /// <summary>Installs again what was installed before and is now missing or changed (one confirmation lists the downloads).</summary>
    public async Task<InstallOutcome> UpdateAsync(CancellationToken cancellationToken)
    {
        var codes = await InstalledAsync(cancellationToken).ConfigureAwait(true);
        logger.LogInformation("Components update: {Count} were installed before ({Codes})", codes.Count, string.Join(", ", codes));
        return codes.Count == 0 ? new InstallOutcome(true, string.Empty) : await flow.EnsureAsync(codes, PurposeKey, null, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Sets the components folder aside, installs again what was installed into a new folder, then deletes the old folder for good. Not ready (declined, cancelled, failed) = the old folder is back in its place.
    /// </summary>
    public async Task<InstallOutcome> ReinstallAsync(CancellationToken cancellationToken)
    {
        var codes = await InstalledAsync(cancellationToken).ConfigureAwait(true);
        var tools = paths.ToolsDirectory;
        logger.LogInformation("Components reinstall: {Count} were installed ({Codes})", codes.Count, string.Join(", ", codes));
        if (!Directory.Exists(tools))
        {
            return codes.Count == 0 ? new InstallOutcome(true, string.Empty) : await flow.EnsureAsync(codes, PurposeKey, null, cancellationToken).ConfigureAwait(true);
        }

        if (!await Task.Run(() => speech.StopIfIdle() & python.StopIfIdle(), CancellationToken.None).ConfigureAwait(true))
        {
            logger.LogInformation("Components reinstall: the speech worker is speaking; nothing was changed");
            return new InstallOutcome(false, text["components.reinstall.busy"]);
        }

        // 0.1.27: a library this process loaded from the folder (PDFium, the OCR's DirectML runtime) stays loaded until the app ends, so the
        // folder cannot be set aside: the user is told to restart first.
        if (planner.All.Where(c => c.LoadedInProcess).Select(c => c.Name).ToList() is { Count: > 0 } loaded)
        {
            logger.LogInformation("Components reinstall: {Components} loaded by this process; nothing was changed", string.Join(", ", loaded));
            var reason = text.Format("components.loaded_restart", string.Join(", ", loaded));
            activity.Add(ActivityLevel.Warning, reason);
            return new InstallOutcome(false, reason);
        }

        // 0.1.37: while this runs (and, through its marker file, after a crash in the middle) the start-up sweep leaves the old folders alone.
        using var hold = gate.Reinstall();
        var aside = Path.TrimEndingDirectorySeparator(tools) + AsidePrefix + Stamp();
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

        logger.LogInformation("Components reinstall: the old folder is set aside; installing {Count} component(s) into a new one", codes.Count);
        InstallOutcome outcome;
        try
        {
            await planner.RefreshAllAsync(cancellationToken).ConfigureAwait(true);
            outcome = codes.Count == 0 ? new InstallOutcome(true, string.Empty) : await flow.EnsureAsync(codes, PurposeKey, null, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "Components reinstall: the install stopped; the old folder goes back");
            outcome = new InstallOutcome(false, text.Format("install.failed", ex.Message));
        }

        if (!outcome.Ready)
        {
            return await PutBackAsync(tools, aside, outcome).ConfigureAwait(true);
        }

        activity.Add(ActivityLevel.Info, text["components.reinstall.done"]);
        await RemoveOldAsync(aside).ConfigureAwait(true);
        return outcome;
    }

    /// <summary>
    /// The new install ended ready: the old folder is deleted for good (0.1.28: no recycle bin; an old components folder is gigabytes, the
    /// bin would free nothing). A failure is shown in the activity panel with where the folder stays (the new components work either way).
    /// </summary>
    private async Task RemoveOldAsync(string aside)
    {
        try
        {
            await discard.DiscardAsync(aside, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("The old components folder was deleted permanently");
            activity.Add(ActivityLevel.Info, text["components.reinstall.deleted"]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(LogEvents.OnScreen, ex, "The old components folder could not be deleted");
            activity.Add(ActivityLevel.Error, text.Format("components.reinstall.delete_failed", ex.Message, Location(aside)));
        }
    }

    /// <summary>
    /// The new install did not end ready: what it left goes aside (then is deleted for good) and the old folder goes back in its
    /// place, so the components are as they were and the question comes again at the next start.
    /// </summary>
    private async Task<InstallOutcome> PutBackAsync(string tools, string aside, InstallOutcome outcome)
    {
        string? failed = null;
        try
        {
            if (Directory.Exists(tools))
            {
                failed = Path.TrimEndingDirectorySeparator(tools) + FailedPrefix + Stamp();
                Directory.Move(tools, failed);
            }

            Directory.Move(aside, tools);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(LogEvents.OnScreen, ex, "The old components folder could not be put back");
            var line = text.Format("components.reinstall.not_back", Location(aside));
            activity.Add(ActivityLevel.Error, line);
            return new InstallOutcome(false, Join(outcome.Reason, line));
        }

        logger.LogInformation("Components reinstall did not finish ({Reason}); the old folder is back in its place", outcome.Reason);
        if (failed is not null)
        {
            await DiscardFailedAsync(failed).ConfigureAwait(true);
        }

        return new InstallOutcome(false, Join(outcome.Reason, text["components.reinstall.kept"]));
    }

    /// <summary>
    /// Deletes what an unfinished new install left (only downloads) for good; a failure is shown in the activity panel with where it stays
    /// (the storage page lists it as an old components folder).
    /// </summary>
    private async Task DiscardFailedAsync(string folder)
    {
        try
        {
            await discard.DiscardAsync(folder, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(LogEvents.OnScreen, ex, "The unfinished new components folder could not be deleted; it stays next to the components folder");
            activity.Add(ActivityLevel.Error, text.Format("components.reinstall.delete_failed", ex.Message, Location(folder)));
        }
    }

    private string Stamp() => clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

    private static string Join(string first, string second) => first.Length == 0 ? second : first + " " + second;
}
