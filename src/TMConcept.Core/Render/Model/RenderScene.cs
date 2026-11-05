namespace TMConcept.Core.Render;

/// <summary>One scene of the render input. Timing is computed in C#; the engine code only draws.</summary>
public sealed class RenderScene
{
    /// <summary>Scene number (1-based).</summary>
    public int Number { get; set; }

    /// <summary>First frame.</summary>
    public int StartFrame { get; set; }

    /// <summary>Number of frames.</summary>
    public int FrameCount { get; set; }

    /// <summary>Start in seconds.</summary>
    public double StartSeconds { get; set; }

    /// <summary>Duration in seconds.</summary>
    public double DurationSeconds { get; set; }

    /// <summary>What is shown (the scene line of the concept).</summary>
    public string Visual { get; set; } = string.Empty;

    /// <summary>What is said (narration = caption text).</summary>
    public string Caption { get; set; } = string.Empty;

    /// <summary>Image of the scene, relative to the work folder (<c>images/3.png</c>); empty when none.</summary>
    public string Image { get; set; } = string.Empty;
}
