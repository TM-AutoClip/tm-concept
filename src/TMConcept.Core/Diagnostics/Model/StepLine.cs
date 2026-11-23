namespace TMConcept.Core.Diagnostics;

/// <summary>One measured step waiting for the log (see <see cref="IStepTimer.Attach"/>).</summary>
public readonly record struct StepLine(string Name, long Milliseconds, ConnectionTotals? Connections);
