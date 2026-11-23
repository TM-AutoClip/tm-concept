namespace TMConcept.Desktop.Startup;

/// <summary>How a run of start steps ended (<see cref="IStartupSequence"/>).</summary>
public enum StartupOutcome
{
    /// <summary>Every step ended well: what comes next (the main window) may open.</summary>
    Completed,

    /// <summary>A step failed: the splash window shows why, nothing opens after it.</summary>
    Failed,

    /// <summary>A step failed and its own handler took over (the unlock window opens instead); the splash shows no error.</summary>
    Redirected,

    /// <summary>The start was canceled (the process is ending).</summary>
    Canceled,
}
