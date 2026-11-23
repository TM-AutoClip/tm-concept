namespace TMConcept.Core.Styles;

/// <summary>
/// The colour palettes a template can pick. The first one is the look of every video before templates (<see cref="Render.RenderTheme"/>:
/// navy background, amber accent, white text). Ids are stored in templates and never change; a palette is only ever added.
/// </summary>
public static class VideoPalettes
{
    /// <summary>Id of the default palette (amber on navy).</summary>
    public const string DefaultId = "amber-navy";

    /// <summary>
    /// The palette id of a template with its own colours (0.1.24: <see cref="VideoStyle.CustomPalette"/>, made in the editor or taken from a
    /// <see cref="SavedPalette"/>).
    /// </summary>
    public const string CustomId = "custom";

    /// <summary>Every palette, in the order of the template editor.</summary>
    public static readonly IReadOnlyList<VideoPalette> All =
    [
        new(DefaultId, "#0B1633", "#16244A", "#FFB224", "#4FC3F7", "#FFFFFF", "#A9B4CC", Light: false),
        new("crimson", "#1A0B0E", "#2A1216", "#E5383B", "#FFB4A2", "#FFF5F2", "#B9A3A6", Light: false),
        new("ocean", "#06202B", "#0D3343", "#2EC4F1", "#9BF6FF", "#F1FBFF", "#9DB8C4", Light: false),
        new("forest", "#0E1F17", "#183226", "#5BD17A", "#F2C94C", "#F4FFF6", "#A4BBAA", Light: false),
        new("mono", "#111111", "#222222", "#FFFFFF", "#9E9E9E", "#F5F5F5", "#8A8A8A", Light: false),
        new("paper", "#F6F1E7", "#FFFFFF", "#D9480F", "#1C7ED6", "#1F1F1F", "#6B6259", Light: true),
        // 0.1.32: the palettes the drawing styles suggest (DrawStyleDefinition.PaletteId).
        new("blueprint", "#0B3D91", "#134FA8", "#FFD166", "#8FC7FF", "#E8F4FF", "#A9C4EA", Light: false),
        new("hologram", "#04151F", "#0A2633", "#49E8FF", "#FFB03A", "#E6FBFF", "#6FB7CB", Light: false),
        new("terminal", "#0A0A0A", "#161616", "#33FF66", "#FFB000", "#C8FFD4", "#6FAE7E", Light: false),
        new("sketch", "#F2EAD8", "#FFFDF6", "#B85C3F", "#3F6FAE", "#2A2420", "#6F6A63", Light: true),
    ];
}
