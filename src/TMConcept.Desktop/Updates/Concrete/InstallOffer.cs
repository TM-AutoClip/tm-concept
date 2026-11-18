using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// The one question at the first start of the portable program (0.1.26, the developer): "Install TM Concept?". Asked once, whatever the
/// answer (Yes runs <see cref="EditionInstaller"/>; No keeps the portable copy); Settings → General still offers the install. Not asked with a
/// custom data folder (<c>--data-dir</c>: a copy kept on a stick, for example), nor where the install is not available, nor when the installed
/// edition is already on this computer (installed with Setup from the browser: the portable copy is a second way in, not a new user).
/// </summary>
public sealed class InstallOffer(EditionInstaller installer, UiPreferencesFile preferences, AppPaths paths, IConfirmDialog confirm, Localizer text,
    ILogger<InstallOffer> logger)
{
    /// <summary>Whether the app runs with a custom data folder (<c>--data-dir</c>): then the question is not asked.</summary>
    public bool CustomDataFolder { get; init; } = paths.IsPortable;

    /// <summary>Whether the question is due now.</summary>
    public bool Due => installer.Available && !CustomDataFolder && !preferences.Read().InstallOfferAnswered && installer.InstalledVersion is null;

    /// <summary>Asks once (when <see cref="Due"/>) and records that it was asked; true when the user chose to install.</summary>
    public async Task<bool> AskAsync()
    {
        if (!Due)
        {
            return false;
        }

        var answer = await confirm.AskAsync(text["edition.offer.title"], text["edition.offer.message"], text["edition.offer.install"], danger: false,
            cancelText: text["edition.offer.keep"]).ConfigureAwait(true);
        try
        {
            preferences.Change(p => p.InstallOfferAnswered = true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The answer to the install question could not be kept; it may be asked again at the next start");
        }

        logger.LogInformation("Install question at first start: {Answer}", answer.Confirmed ? "install" : "stay portable");
        return answer.Confirmed;
    }
}
