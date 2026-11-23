namespace TMConcept.Desktop.Startup;

/// <summary>
/// Runs the start's steps in order, each off the interface thread, and shows each one's stage on the splash window; the splash window's
/// Cancel ends the app (<see cref="Cancel"/>).
/// </summary>
public interface IStartupSequence
{
    /// <summary>Whether a step failed (the splash shows the error; Cancel then closes with exit code 1).</summary>
    bool Failed { get; }

    /// <summary>
    /// Runs <paramref name="steps"/> in order; the first failure stops the run (later steps do not run). An unhandled failure is shown on
    /// <paramref name="screen"/>.
    /// </summary>
    Task<StartupOutcome> RunAsync(IReadOnlyList<StartupStep> steps, IStartupScreen screen);

    /// <summary>
    /// Ends the start: the child processes, then the data folder's lock, then the process. Safe at any step: the database is upgraded on a
    /// working copy that replaces the file only once it is complete, so a cut-short upgrade leaves the file as it was.
    /// </summary>
    void Cancel();
}
