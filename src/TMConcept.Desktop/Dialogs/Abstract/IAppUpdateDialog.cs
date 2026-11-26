using TMConcept.Desktop.Updates;

namespace TMConcept.Desktop.Dialogs;

/// <summary>Offers a newer version of the app: its version and notes; installs it only when the user confirms (then the app restarts).</summary>
public interface IAppUpdateDialog
{
    /// <summary>
    /// Shows <paramref name="offer"/> until it is installed (the app restarts), closed or put off. True when it was installed: the app is
    /// closing to start the new version, nothing more may be asked.
    /// </summary>
    Task<bool> ShowAsync(UpdateOffer offer);
}
