namespace TMConcept.Desktop.Dialogs;

/// <summary>Asks what to do with the installed components after a new app version started: update them, reinstall them, or later.</summary>
public interface IUpdateDialog
{
    /// <summary>
    /// Shows the question: <paramref name="from"/> is the version that ran before (empty = not known: a version before 0.1.16 did not record
    /// it), <paramref name="to"/> the running one, <paramref name="toolsFolder"/> where the components live (as shown to the user). Closed
    /// without a choice = <see cref="UpdateChoice.Later"/>.
    /// </summary>
    Task<UpdateChoice> AskAsync(string from, string to, string toolsFolder);
}
