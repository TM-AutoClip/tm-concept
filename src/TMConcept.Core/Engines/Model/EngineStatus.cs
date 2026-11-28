namespace TMConcept.Core.Engines;

/// <summary>Whether an engine is ready; <see cref="Missing"/> lists <see cref="EngineNeeds"/> words.</summary>
public sealed record EngineStatus(bool Ready, IReadOnlyList<string> Missing);
