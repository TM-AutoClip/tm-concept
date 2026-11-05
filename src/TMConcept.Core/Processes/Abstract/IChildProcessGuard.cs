using System.Diagnostics;

namespace TMConcept.Core.Processes;

/// <summary>Ties every process the app starts to the app, so none outlives it (Windows job object; on Linux ended on exit).</summary>
public interface IChildProcessGuard
{
    /// <summary>Takes a just-started process; dispose the result when the process is done. Never throws.</summary>
    IDisposable Adopt(Process process);
}
