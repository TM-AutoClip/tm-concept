using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Updates;
using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Dialogs;

/// <summary><see cref="IAppUpdateDialog"/> as a modal window over the active window (none = nothing is shown).</summary>
public sealed class AppUpdateDialog(WindowHost host, AppUpdater updater, IShellLauncher shell, Localizer text, ILogger<AppUpdateDialog> logger) : IAppUpdateDialog
{
    /// <inheritdoc />
    public async Task ShowAsync(UpdateOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (host.Active is not { } owner)
        {
            logger.LogInformation("A newer version ({Version}) was found but no window is open to offer it", offer.Version);
            return;
        }

        logger.LogInformation("Offering version {Version} (installable: {Installable})", offer.Version, offer.CanInstall);
        using var model = new AppUpdateViewModel(offer, updater, shell, text, logger);
        var window = new AppUpdateWindow { DataContext = model };
        model.CloseRequested += (_, _) => window.Close();
        await window.ShowDialog(owner).ConfigureAwait(true);
    }
}
