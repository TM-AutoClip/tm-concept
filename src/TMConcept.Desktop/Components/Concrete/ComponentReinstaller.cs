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
/// The components after a new app version, or on request (General settings). What "was installed" comes from the install record
/// (<see cref="IInstallRecord"/>, kept outside the components folder, with the app version of each install) and from the folders that hold
/// an install; an empty folder or a partial download does not count.
/// <list type="bullet">
/// <item><see cref="UpdateAsync"/> installs again, through the one install flow (<see cref="ComponentInstallFlow"/>: one confirmation, the
/// install window), what was installed before and is now missing or changed; nothing else is touched.</item>
/// <item><see cref="ReinstallAsync"/> ends the idle speech worker (it keeps the speech libraries open for a minute after the last scene),
/// renames the whole components folder (<c>&lt;data&gt;/tools</c>) aside in one step (a file in use makes the rename fail and nothing
/// changes), installs again what was there into a new, empty folder, and only when that ended ready moves the old folder to the recycle bin
/// (when that fails, a permanent delete only after its own confirmation that says so; declined, the old folder stays aside and the message
/// says where). A declined, cancelled or failed install puts the old folder back in its place (0.1.16 removed it first, so a declined
/// download left nothing installed and the next start did not ask again).</item>
/// </list>
/// The database, the profiles, the concepts and the videos are outside that folder and never touched.
/// </summary>
public sealed class ComponentReinstaller(ComponentPlanner planner, ComponentInstallFlow flow, IRecycleBin bin, IConfirmDialog confirm, AppPaths paths,
    Speech.Speech.SpeechWorkerClient speech, IActivityLog activity, Localizer text, TimeProvider clock, ILogger<ComponentReinstaller> logger)
{
    /// <summary>The text of what an install confirmation says a cancel means here.</summary>
    public const string PurposeKey = "install.purpose.update";

    /// <summary>The start of the name the old components folder gets while the new one is installed (<c>tools.old-20261116-120000</c>).</summary>
    public const string AsidePrefix = ".old-";

    /// <summary>The start of the name a new components folder gets when its install did not finish and the old one goes back.</summary>
    public const string FailedPrefix = ".failed-";

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
    /// Sets the components folder aside, installs again what was installed into a new folder, then moves the old folder to the recycle bin
    /// (a permanent delete only after its own confirmation). Not ready (declined, cancelled, failed) = the old folder is back in its place.
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

        if (!await Task.Run(speech.StopIfIdle, CancellationToken.None).ConfigureAwait(true))
        {
            logger.LogInformation("Components reinstall: the speech worker is speaking; nothing was changed");
            return new InstallOutcome(false, text["components.reinstall.busy"]);
        }

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
            return PutBack(tools, aside, outcome);
        }

        activity.Add(ActivityLevel.Info, text["components.reinstall.done"]);
        await RemoveOldAsync(aside).ConfigureAwait(true);
        return outcome;
    }

    /// <summary>
    /// The new install ended ready: the old folder goes to the recycle bin, or after its own confirmation is deleted for good. Declined or
    /// failed, it stays aside (the new components work either way) and the activity line says where it is.
    /// </summary>
    private async Task RemoveOldAsync(string aside)
    {
        try
        {
            await bin.MoveAsync(aside, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("The old components folder was moved to the recycle bin");
            activity.Add(ActivityLevel.Info, text["components.reinstall.moved"]);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException
            or OperationCanceledException)
        {
            logger.LogWarning(ex, "The old components folder could not be moved to the recycle bin");
            var answer = await confirm.AskAsync(text["components.reinstall.bin_failed.title"],
                text.Format("components.reinstall.bin_failed", ex.Message, Location(aside)), text["components.reinstall.delete"], danger: true).ConfigureAwait(true);
            if (!answer.Confirmed)
            {
                logger.LogInformation("The permanent delete of the old components folder was declined; it stays aside");
                activity.Add(ActivityLevel.Warning, text.Format("components.reinstall.old_kept", Location(aside)));
                return;
            }
        }

        try
        {
            if (Directory.Exists(aside))
            {
                await Task.Run(() => Directory.Delete(aside, recursive: true)).ConfigureAwait(true);
            }

            logger.LogInformation("The old components folder was deleted permanently");
            activity.Add(ActivityLevel.Info, text["components.reinstall.deleted"]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(LogEvents.OnScreen, ex, "The old components folder could not be deleted");
            activity.Add(ActivityLevel.Error, text.Format("components.reinstall.failed", ex.Message));
        }
    }

    /// <summary>
    /// The new install did not end ready: what it left goes aside (then to the recycle bin when it can) and the old folder goes back in its
    /// place, so the components are as they were and the question comes again at the next start.
    /// </summary>
    private InstallOutcome PutBack(string tools, string aside, InstallOutcome outcome)
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
            _ = DiscardAsync(failed);
        }

        return new InstallOutcome(false, Join(outcome.Reason, text["components.reinstall.kept"]));
    }

    /// <summary>What an unfinished new install left: to the recycle bin, or left aside with a log line (it is only downloads).</summary>
    private async Task DiscardAsync(string folder)
    {
        try
        {
            await bin.MoveAsync(folder, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException
            or OperationCanceledException)
        {
            logger.LogWarning(ex, "The unfinished new components folder could not be moved to the recycle bin; it stays next to the components folder");
        }
    }

    private string Stamp() => clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

    private static string Join(string first, string second) => first.Length == 0 ? second : first + " " + second;
}
