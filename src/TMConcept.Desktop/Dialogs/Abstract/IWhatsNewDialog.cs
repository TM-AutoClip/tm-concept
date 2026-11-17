using TMConcept.Desktop.Updates;

namespace TMConcept.Desktop.Dialogs;

/// <summary>Shows the "What's new" window: the notes of the given versions, newest first, in the app's language.</summary>
public interface IWhatsNewDialog
{
    /// <summary>Shows <paramref name="versions"/> until the window is closed (nothing when there is no window to show it over).</summary>
    Task ShowAsync(IReadOnlyList<VersionNotes> versions);
}
