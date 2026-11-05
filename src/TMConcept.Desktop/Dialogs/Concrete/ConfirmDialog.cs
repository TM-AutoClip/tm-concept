using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Dialogs;

/// <summary><see cref="IConfirmDialog"/> as a small modal window over the active window.</summary>
public sealed class ConfirmDialog(WindowHost host, Localizer text) : IConfirmDialog
{
    /// <inheritdoc />
    public async Task<ConfirmResult> AskAsync(string title, string message, string confirmText, bool danger, string? checkbox = null)
    {
        var model = new ConfirmViewModel(title, message, confirmText, text["common.cancel"], danger, checkbox);
        var window = new ConfirmWindow { DataContext = model };
        if (host.Active is { } owner)
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
        }
        else
        {
            return new ConfirmResult(false, false);
        }

        return new ConfirmResult(model.Confirmed, model.Checked);
    }
}
