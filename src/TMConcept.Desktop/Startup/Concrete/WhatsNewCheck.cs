using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Updates;

namespace TMConcept.Desktop.Startup;

/// <summary>
/// The "What's new" window (0.1.23, the developer): at the first start after the app version changed (an update or a replaced program file)
/// it shows the notes of every version after the one that ran before up to the running one, newest first, in the app's language, from the
/// notes built into the app (no network). The version that ran before is the one the version check keeps (<see cref="UiPreferences.LastVersion"/>,
/// read before this start changes it), so it works from 0.1.17 on, which recorded nothing new. A new install (no version known) shows nothing.
/// The notes shown are remembered (<see cref="UiPreferences.WhatsNewSeenVersion"/>): they show once. Settings → General opens the window at
/// any time with every version.
/// </summary>
public sealed class WhatsNewCheck(UiPreferencesFile preferences, IWhatsNewDialog dialog, ILogger<WhatsNewCheck> logger)
{
    /// <summary>The versions the start shows for <paramref name="previous"/> (the version that ran before) and <paramref name="current"/>.</summary>
    public IReadOnlyList<VersionNotes> Pending(string? previous, string current) =>
        WhatsNewRange.Versions(previous, preferences.Read().WhatsNewSeenVersion, current, BundledReleaseNotes.All);

    /// <summary>
    /// At start: shows the notes of the versions after <paramref name="previous"/> up to <paramref name="current"/> once, and remembers
    /// <paramref name="current"/> (also when there is nothing to show, so a later start never shows older notes).
    /// </summary>
    public async Task RunAsync(string? previous, string current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var versions = Pending(previous, current);
        if (!string.Equals(preferences.Read().WhatsNewSeenVersion, current, StringComparison.Ordinal))
        {
            Remember(current);
        }

        if (versions.Count == 0)
        {
            return;
        }

        logger.LogInformation("App version {From} -> {To}: showing the notes of {Count} new versions", previous, current, versions.Count);
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
