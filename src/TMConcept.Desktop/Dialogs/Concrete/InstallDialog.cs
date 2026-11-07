using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Dialogs;

/// <summary>
/// <see cref="IInstallDialog"/> as a modal window over the active window. Without a shown window (before the main window exists) the install
/// runs without one.
/// </summary>
public sealed class InstallDialog(WindowHost host) : IInstallDialog
{
    /// <inheritdoc />
    public async Task<string> RunAsync(InstallViewModel model, Func<InstallViewModel, Task<string>> install)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(install);
        if (host.Active is not { IsVisible: true } owner)
        {
            var alone = await install(model).ConfigureAwait(true);
            model.Finish(alone);
            return alone;
        }

        var window = new InstallWindow { DataContext = model };
        var shown = window.ShowDialog(owner);
        string error;
        try
        {
            error = await install(model).ConfigureAwait(true);
        }
        catch
        {
            // An unexpected failure must not leave the modal window over the app.
            model.Finish(string.Empty);
            throw;
        }

        // Installed: the window closes by itself. Failed: it shows the reason and stays until the user closes it.
        model.Finish(error);
        await shown.ConfigureAwait(true);
        return error;
    }
}
