namespace TMConcept.Core.Styles;

/// <summary>
/// The colour palettes a template can pick. The first one is the look of every video before templates (<see cref="Render.RenderTheme"/>:
/// navy background, amber accent, white text). Ids are stored in templates and never change; a palette is only ever added.
/// </summary>
public static class VideoPalettes
{
    /// <summary>Id of the default palette (amber on navy).</summary>
    public const string DefaultId = "amber-navy";

    /// <summary>Every palette, in the order of the template editor.</summary>
    public static readonly IReadOnlyList<VideoPalette> All =
    [
        new(DefaultId, "#0B1633", "#16244A", "#FFB224", "#4FC3F7", "#FFFFFF", "#A9B4CC", Light: false),
        new("crimson", "#1A0B0E", "#2A1216", "#E5383B", "#FFB4A2", "#FFF5F2", "#B9A3A6", Light: false),
        new("ocean", "#06202B", "#0D3343", "#2EC4F1", "#9BF6FF", "#F1FBFF", "#9DB8C4", Light: false),
        new("forest", "#0E1F17", "#183226", "#5BD17A", "#F2C94C", "#F4FFF6", "#A4BBAA", Light: false),
        new("mono", "#111111", "#222222", "#FFFFFF", "#9E9E9E", "#F5F5F5", "#8A8A8A", Light: false),
        new("paper", "#F6F1E7", "#FFFFFF", "#D9480F", "#1C7ED6", "#1F1F1F", "#6B6259", Light: true),
    ];
}
