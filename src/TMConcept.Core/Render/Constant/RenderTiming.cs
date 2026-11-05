namespace TMConcept.Core.Render;

/// <summary>Timing rules of the render input.</summary>
public static class RenderTiming
{
    /// <summary>Closing margin after the last scene, so the picture does not end with the last word.</summary>
    public const int ClosingMs = 1000;

    /// <summary>Shortest scene.</summary>
    public const int SceneMinMs = 1000;

    /// <summary>Longest video of the first version (three minutes).</summary>
    public const int VideoMaxMs = 180_000;
}
