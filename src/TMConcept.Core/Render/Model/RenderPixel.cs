namespace TMConcept.Core.Render;

/// <summary>
/// The pixel layer of the app's drawer (F13, 0.1.51; input.json <c>style.pixel</c>, written by <see cref="RenderPixelBuilder"/> only for the
/// pixel art style; absent otherwise, and then <c>plan.js</c> draws no pixel layer). The scene's ground (its picture, or a dithered sky
/// and hills when there is none) is drawn on a <see cref="Width"/> × <see cref="Height"/> buffer, coloured from <see cref="Ramps"/> with
/// ordered dithering and blown up without smoothing; it changes <see cref="Steps"/> times a second.
/// </summary>
public sealed class RenderPixel
{
    /// <summary>The buffer's width in art pixels (480 across, 270 upright).</summary>
    public int Width { get; set; }

    /// <summary>The buffer's height in art pixels (270 across, 480 upright).</summary>
    public int Height { get; set; }

    /// <summary>Steps a second of the pixel layer and of the pixel transitions.</summary>
    public int Steps { get; set; }

    /// <summary>The height of a row of the blocks transition, in art pixels.</summary>
    public int Block { get; set; }

    /// <summary>
    /// The colour ramps, dark to light, by palette token (<c>background</c>, <c>surface</c>, <c>accent</c>, <c>accent2</c>, <c>text</c>,
    /// <c>muted</c>); the middle colour of each is the palette's own.
    /// </summary>
    public Dictionary<string, List<string>> Ramps { get; set; } = [];
}
