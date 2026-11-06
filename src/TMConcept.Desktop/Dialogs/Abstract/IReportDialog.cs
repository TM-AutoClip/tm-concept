namespace TMConcept.Desktop.Dialogs;

/// <summary>Shows the "Report a problem" window over the active window.</summary>
public interface IReportDialog
{
    /// <summary>Shows it and returns when it is closed.</summary>
    Task ShowAsync();
}
