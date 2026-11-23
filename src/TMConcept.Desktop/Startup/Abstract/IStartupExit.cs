namespace TMConcept.Desktop.Startup;

/// <summary>How a canceled start ends the app: in this order, the child processes, the data folder's lock, the process.</summary>
public interface IStartupExit
{
    /// <summary>Ends every child process the app started (ffmpeg, Node, Chrome, the speech worker); nothing when none was started yet.</summary>
    void KillChildren();

    /// <summary>Frees the data folder's instance lock, so a new start opens at once.</summary>
    void ReleaseLock();

    /// <summary>Ends the process with <paramref name="exitCode"/>.</summary>
    void End(int exitCode);
}
