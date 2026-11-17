namespace TMConcept.Core.Styles;

/// <summary>
/// The drawing style of the video code when a scene has no image; it also steers the style of generated images so both match (new values are
/// added at the end; stored as numbers).
/// </summary>
public enum DrawStyle
{
    /// <summary>Flat shapes and icons.</summary>
    Flat = 0,

    /// <summary>Stick figures.</summary>
    StickFigure = 1,

    /// <summary>Thin line drawings.</summary>
    LineArt = 2,

    /// <summary>Layered paper cut-outs.</summary>
    PaperCut = 3,

    /// <summary>Charts, numbers and diagrams.</summary>
    Infographic = 4,

    /// <summary>Glowing lines on a dark background.</summary>
    Neon = 5,
}
