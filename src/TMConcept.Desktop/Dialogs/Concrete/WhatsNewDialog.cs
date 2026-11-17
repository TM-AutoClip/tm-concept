using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Updates;
using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Dialogs;

/// <summary><see cref="IWhatsNewDialog"/> as a modal window over the active window (none = nothing is shown, which is logged).</summary>
public sealed class WhatsNewDialog(WindowHost host, Localizer text, ILogger<WhatsNewDialog> logger) : IWhatsNewDialog
{
    /// <inheritdoc />
    public async Task ShowAsync(IReadOnlyList<VersionNotes> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (host.Active is not { } owner)
        {
            logger.LogInformation("The notes of {Count} versions were not shown: no window is open", versions.Count);
            return;
        }

        logger.LogInformation("Showing what is new in {Versions}", string.Join(", ", versions.Select(v => v.Version)));
        var model = new WhatsNewViewModel(versions, text);
        var window = new WhatsNewWindow { DataContext = model };
        model.CloseRequested += (_, _) => window.Close();
        await window.ShowDialog(owner).ConfigureAwait(true);
    }
}
