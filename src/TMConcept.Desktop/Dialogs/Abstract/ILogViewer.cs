namespace TMConcept.Desktop.Dialogs;

/// <summary>Opens the detailed log page (one window; a second call brings it to the front).</summary>
public interface ILogViewer
{
    /// <summary>Shows the log window.</summary>
    void Show();
}
