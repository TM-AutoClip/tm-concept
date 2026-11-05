namespace TMConcept.Core.Engines;

/// <summary>An event read from one output line of an engine process.</summary>
/// <param name="Ratio">Progress 0..1 (null when unknown).</param>
/// <param name="Message">Short message.</param>
/// <param name="Done">The engine finished.</param>
/// <param name="Error">Error text when it failed.</param>
/// <param name="Canceled">The engine stopped after a cancel request.</param>
public sealed record EngineEvent(double? Ratio, string Message, bool Done = false, string? Error = null, bool Canceled = false);
