using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Updates;

namespace TMConcept.Desktop.Startup;

/// <summary>
/// The "What's new" window (0.1.23, the developer): at the first start after the app version changed (an update or a replaced program file)
/// it shows the notes of every version after the newest one a start recorded (<see cref="UiPreferences.WhatsNewSeenVersion"/>) up to the
/// running one, newest first, in the app's language, from the notes built into the app (no network). A new install (no version recorded)
/// shows nothing. Every start records a newer running version, so the notes show once. Settings → General opens the window at any time with
/// every version.
/// </summary>
public sealed class WhatsNewCheck(UiPreferencesFile preferences, IWhatsNewDialog dialog, ILogger<WhatsNewCheck> logger)
{
    /// <summary>The versions the start shows for the running version <paramref name="current"/>.</summary>
    public IReadOnlyList<VersionNotes> Pending(string current) =>
        WhatsNewRange.Versions(preferences.Read().WhatsNewSeenVersion, current, BundledReleaseNotes.All);

    /// <summary>
    /// At start: shows the notes of the versions after the one recorded up to <paramref name="current"/> once, and remembers
    /// <paramref name="current"/> when it is newer than the version remembered (also when there is nothing to show, so a later start never
    /// shows older notes; never lowered by going back to an older version, so going forward again does not show the same notes twice).
    /// </summary>
    public async Task RunAsync(string current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var seen = preferences.Read().WhatsNewSeenVersion;
        var versions = WhatsNewRange.Versions(seen, current, BundledReleaseNotes.All);
        if (AppVersion.TryParse(current, out var running) && (!AppVersion.TryParse(seen, out var remembered) || running > remembered))
        {
            Remember(current);
        }

        if (versions.Count == 0)
        {
            return;
        }

        logger.LogInformation("App version {From} -> {To}: showing the notes of {Count} new versions", seen, current, versions.Count);
        await dialog.ShowAsync(versions).ConfigureAwait(true);
    }

    /// <summary>Settings → General: the notes of every version up to <paramref name="current"/>, newest first.</summary>
    public Task ShowAllAsync(string current) => dialog.ShowAsync(WhatsNewRange.UpTo(current, BundledReleaseNotes.All));

    private void Remember(string current)
    {
        try
        {
            preferences.Change(p => p.WhatsNewSeenVersion = current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The notes may show again at the next start; nothing else depends on it.
            logger.LogWarning(ex, "The version whose notes were shown could not be recorded in the preferences");
        }
    }
}
