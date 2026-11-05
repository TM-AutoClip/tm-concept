namespace TMConcept.Core.Processes;

/// <summary>
/// Runs an external program (node, ffmpeg, tar…) with a clean environment: variables that look like keys, tokens or passwords are removed
/// before the child starts, so a model-written script can never read them.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs and waits; every output line goes to <paramref name="onLine"/> (standard output and error).</summary>
    Task<ProcessResult> RunAsync(ProcessStart start, Action<string>? onLine, CancellationToken cancellationToken);
}
