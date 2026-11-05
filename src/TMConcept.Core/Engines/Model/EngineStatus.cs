namespace TMConcept.Core.Engines;

/// <summary>Whether an engine is ready; <see cref="Missing"/> lists "node", "packages", "browser" or "ffmpeg".</summary>
public sealed record EngineStatus(bool Ready, IReadOnlyList<string> Missing);
