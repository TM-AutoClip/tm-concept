namespace TMConcept.Core.References;

/// <summary>How the camera moves of a reference start and stop (S1, 0.1.52; stored as numbers, new values are added at the end).</summary>
public enum EaseShape
{
    /// <summary>Slow at both ends (or no camera move).</summary>
    InOut = 0,

    /// <summary>Fast at the start, slowing down.</summary>
    Out = 1,

    /// <summary>Slow at the start, speeding up.</summary>
    In = 2,
}
