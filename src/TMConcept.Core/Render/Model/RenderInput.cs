namespace TMConcept.Core.Render;

/// <summary>
/// The shared input of every engine: Remotion component props, HyperFrames <c>window.INPUT</c>, Revideo
/// <c>useScene().variables.get('input')</c>. The model writes the code once; the code reads text and timing from here (text is never
/// embedded in the code). JSON is camelCase; the TypeScript type in the engine skeletons matches it field by field.
/// </summary>
public sealed class RenderInput
{
    /// <summary>Width in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; set; }

    /// <summary>Frames per second.</summary>
    public int Fps { get; set; } = 30;

    /// <summary>Total frames.</summary>
    public int TotalFrames { get; set; }

    /// <summary>Duration in seconds.</summary>
    public double DurationSeconds { get; set; }

    /// <summary>Content language (ISO code).</summary>
    public string Language { get; set; } = "en";

    /// <summary>Title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Colors.</summary>
    public RenderTheme Theme { get; set; } = new();

    /// <summary>
    /// The template resolved for the code (<see cref="RenderStyleBuilder"/>); <c>null</c> for a video made before templates, and then left out of
    /// the file, so its input.json stays as it was.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RenderStyle? Style { get; set; }

    /// <summary>Scenes.</summary>
    public List<RenderScene> Scenes { get; set; } = [];
}
