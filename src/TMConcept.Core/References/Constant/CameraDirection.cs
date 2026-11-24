namespace TMConcept.Core.References;

/// <summary>Which way the camera of a reference moves most (S1, 0.1.52; stored as numbers, new values are added at the end).</summary>
public enum CameraDirection
{
    /// <summary>No camera move worth naming.</summary>
    None = 0,

    /// <summary>The camera moves left (the picture slides right).</summary>
    Left = 1,

    /// <summary>The camera moves right.</summary>
    Right = 2,

    /// <summary>The camera moves up.</summary>
    Up = 3,

    /// <summary>The camera moves down.</summary>
    Down = 4,
}
