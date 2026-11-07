namespace TMConcept.Desktop.Dialogs;

/// <summary>
/// Shows an install as a modal window over the app while it runs (the app cannot be used meanwhile): the window closes by itself when
/// everything is installed; after a failure it shows the reason and stays until the user closes it. Then the waiting work goes on.
/// </summary>
public interface IInstallDialog
{
    /// <summary>
    /// Runs <paramref name="install"/> with the window of <paramref name="model"/> open; <paramref name="install"/> fills the model and
    /// returns the error text (empty = installed). Returns that text when the window is closed.
    /// </summary>
    Task<string> RunAsync(InstallViewModel model, Func<InstallViewModel, Task<string>> install);
}
