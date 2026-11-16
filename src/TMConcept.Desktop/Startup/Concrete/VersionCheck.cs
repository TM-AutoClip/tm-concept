using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Startup;

/// <summary>
/// After the main window opened (never in the speech worker, never before the first run or the unlock): when another app version ran
/// before and components are installed, the user chooses to update them (the default), reinstall them from scratch, or later
/// (<see cref="IUpdateDialog"/>; the developer, 16 Nov 2026: a new version can pin newer downloads, and a tester's half-installed image
/// runtime stayed broken across versions). The version is recorded in <c>preferences.json</c> (<see cref="UiPreferences.LastVersion"/>) only
/// after an update or a reinstall that ended ready, or when nothing is installed: "later", a declined or failed download and a kept folder
/// ask again at the next start. The data is never touched.
/// </summary>
public sealed class VersionCheck(UiPreferencesFile preferences, ComponentReinstaller components, IUpdateDialog dialog, IActivityLog activity, Localizer text,
    ILogger<VersionCheck> logger)
{
    /// <summary>What to do for the <paramref name="recorded"/> version (empty = none) and the <paramref name="current"/> one.</summary>
    public static VersionChange Decide(string? recorded, string current, bool componentsInstalled) =>
        string.Equals(recorded ?? string.Empty, current, StringComparison.Ordinal) ? VersionChange.Same
        : componentsInstalled ? VersionChange.Ask
        : VersionChange.Record;

    /// <summary>Checks the version that ran before against <paramref name="current"/> and acts on it.</summary>
    public async Task RunAsync(string current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        var recorded = preferences.Read().LastVersion;
        if (Decide(recorded, current, componentsInstalled: false) == VersionChange.Same)
        {
            return;
        }

        var installed = await components.InstalledAsync(cancellationToken).ConfigureAwait(true);
        if (Decide(recorded, current, installed.Count > 0) == VersionChange.Record)
        {
            logger.LogInformation("App version {From} -> {To}: no component is installed, the version is recorded", Shown(recorded), current);
            Remember(current);
            return;
        }

        logger.LogInformation("App version {From} -> {To} with {Count} installed components: asking what to do with them", Shown(recorded), current,
            installed.Count);
        var choice = await dialog.AskAsync(recorded, current, components.Location()).ConfigureAwait(true);
        logger.LogInformation("After the version change the user chose: {Choice}", choice);
        switch (choice)
        {
            case UpdateChoice.Update:
                Finish(await components.UpdateAsync(cancellationToken).ConfigureAwait(true), current);
                break;
            case UpdateChoice.Reinstall:
                Finish(await components.ReinstallAsync(cancellationToken).ConfigureAwait(true), current);
                break;
            default:
                // Asked again at the next start: the version is not recorded.
                activity.Add(ActivityLevel.Info, text["update.later"]);
                break;
        }
    }

    private static string Shown(string recorded) => recorded.Length > 0 ? recorded : "(not recorded)";

    /// <summary>
    /// Says how the update or reinstall ended. The version is recorded only when it ended ready: a declined download, a network failure or
    /// a folder that was kept leaves the old components in place, so the question comes again at the next start (0.1.16 recorded it in every
    /// case and a broken component was never offered again).
    /// </summary>
    private void Finish(InstallOutcome outcome, string current)
    {
        if (outcome.Ready)
        {
            activity.Add(ActivityLevel.Info, text.Format("update.done", current));
            Remember(current);
            return;
        }

        logger.LogInformation("The components were not updated; the version is not recorded and the question comes again at the next start");
        if (outcome.Reason.Length > 0)
        {
            activity.Add(ActivityLevel.Warning, outcome.Reason);
        }
    }

    private void Remember(string current)
    {
        try
        {
            preferences.Change(p => p.LastVersion = current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The question comes again at the next start; nothing else depends on it.
            logger.LogWarning(ex, "The app version could not be recorded in the preferences");
        }
    }
}
