using TMConcept.Desktop.Updates;

namespace TMConcept.Desktop.Dialogs;

/// <summary>Offers a newer version of the app: its version and notes; installs it only when the user confirms (then the app restarts).</summary>
public interface IAppUpdateDialog
{
    /// <summary>Shows <paramref name="offer"/> until it is installed (the app restarts), closed or put off.</summary>
    Task ShowAsync(UpdateOffer offer);
}
