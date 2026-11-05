namespace TMConcept.Core.Processes;

/// <summary>Exit code and the last lines of output (for the log and error messages).</summary>
public sealed record ProcessResult(int ExitCode, string LastLines);
