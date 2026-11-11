namespace TMConcept.Desktop.Dialogs;

/// <summary>Asks the user to confirm an action (permanent deletes, downloads, license notices).</summary>
public interface IConfirmDialog
{
    /// <summary>
    /// Shows the question; true when confirmed. <paramref name="checkbox"/> adds a check box (for example "also move the file to the recycle
    /// bin"); its state is returned in <see cref="ConfirmResult.Checked"/>. <paramref name="cancelText"/> names the other button ("Stay";
    /// null = Cancel).
    /// </summary>
    Task<ConfirmResult> AskAsync(string title, string message, string confirmText, bool danger, string? checkbox = null, string? cancelText = null);
}
