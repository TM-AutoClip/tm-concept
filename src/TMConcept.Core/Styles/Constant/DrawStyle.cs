namespace TMConcept.Core.Styles;

/// <summary>
/// The look of a video: how the video code draws a scene without an image and how the image model is asked to paint one, so both match
/// (new values are added at the end; stored as numbers). Each value has its definition in <see cref="DrawStyles"/>: description, image
/// style, drawing words, craft rules, texture and, for a storytelling style, guidance for the concept. <see cref="Auto"/> (the default of a
/// new template since 0.1.32) lets the model choose a style that fits the topic for each video.
/// </summary>
public enum DrawStyle
{
    /// <summary>Flat shapes and icons.</summary>
    Flat = 0,

    /// <summary>Stick-figure narrator (0.1.32; until then plain stick figures): a stick-figure presenter acts out and points at the subject.</summary>
    StickFigure = 1,

    /// <summary>Thin line drawings.</summary>
    LineArt = 2,

    /// <summary>Layered paper cut-outs.</summary>
    PaperCut = 3,

    /// <summary>Charts, numbers and diagrams.</summary>
    Infographic = 4,

    /// <summary>Glowing lines on a dark background.</summary>
    Neon = 5,

    /// <summary>Pencil sketch ("Kara kalem", 0.1.32): graphite lines and hatching on warm paper.</summary>
    PencilSketch = 6,

    /// <summary>Animation ("Animasyon", 0.1.32): a 2D cartoon look with bold outlines and cel shading.</summary>
    Cartoon = 7,

    /// <summary>Blueprint (0.1.32): white technical drawing on blueprint blue, drawn on line by line.</summary>
    Blueprint = 8,

    /// <summary>Hologram (0.1.32): cyan wireframes and readouts that glow and flicker on a dark screen.</summary>
    Hologram = 9,

    /// <summary>Retro terminal (0.1.32): phosphor characters on a black CRT screen, typed in.</summary>
    Terminal = 10,

    /// <summary>Isometric (0.1.32): small 3D-looking blocks drawn in outline first, then filled.</summary>
    Isometric = 11,

    /// <summary>Watercolour (0.1.32): soft washes with wet edges on textured paper (best with an image model).</summary>
    Watercolor = 12,

    /// <summary>No style given (0.1.32, the default of a new template): the model chooses the style that fits each video's topic.</summary>
    Auto = 13,

    /// <summary>
    /// Pixel art (F13, 0.1.51): the scene drawn on a small buffer (480 × 270, 270 × 480 upright) and blown up four times without smoothing,
    /// colours from ramps of the palette, shading by ordered (Bayer) dithering; scenes change by a dither dissolve, an iris or blocks.
    /// </summary>
    PixelArt = 14,
}
