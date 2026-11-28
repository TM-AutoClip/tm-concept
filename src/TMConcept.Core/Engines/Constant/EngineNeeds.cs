namespace TMConcept.Core.Engines;

/// <summary>What an engine can be missing (<see cref="EngineStatus.Missing"/>; the words also go into the "missing: …" messages and the log).</summary>
public static class EngineNeeds
{
    /// <summary>Node.js.</summary>
    public const string Node = "node";

    /// <summary>ffmpeg + ffprobe.</summary>
    public const string Ffmpeg = "ffmpeg";

    /// <summary>The engine's npm packages.</summary>
    public const string Packages = "packages";

    /// <summary>The browser it renders with (Chrome Headless Shell).</summary>
    public const string Browser = "browser";
}
