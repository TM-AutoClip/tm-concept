namespace TMConcept.Core.Engines;

/// <summary>Code-to-video engine (new values are added at the end).</summary>
public enum EngineKind
{
    /// <summary>HyperFrames (Apache-2.0): HTML + GSAP timeline; the default.</summary>
    HyperFrames = 0,

    /// <summary>Remotion (source-available; companies above three people need a paid license): React components.</summary>
    Remotion = 1,

    /// <summary>Revideo (MIT; experimental): TypeScript generator scenes.</summary>
    Revideo = 2,
}
