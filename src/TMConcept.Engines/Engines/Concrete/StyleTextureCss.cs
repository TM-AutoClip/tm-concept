namespace TMConcept.Engines.Engines;

/// <summary>
/// The texture layer of a style on the HyperFrames page (0.1.32, <see cref="Core.Styles.StyleTexture"/>): one still, faint layer the app lays
/// over the whole picture, above the model's scene and under the subtitles (they are burned in afterwards). Pure CSS with inline SVG noise as
/// a data address (the page's security policy allows data: images), so nothing is downloaded and every frame is the same: the texture never
/// swims. Unknown or empty tokens draw nothing.
/// </summary>
public static class StyleTextureCss
{
    // Fractal noise as an SVG image: low frequency reads as paper fibres, high frequency as film grain.
    private const string PaperNoise = "url(\"data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='400' height='400'><filter id='n'>"
        + "<feTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='3' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0.45 0 0 0 0 0.38 "
        + "0 0 0 0 0.3 0 0 0 0.9 0'/></filter><rect width='100%' height='100%' filter='url(%23n)'/></svg>\")";

    private const string GrainNoise = "url(\"data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='300' height='300'><filter id='g'>"
        + "<feTurbulence type='fractalNoise' baseFrequency='2.2' numOctaves='2' stitchTiles='stitch'/><feColorMatrix type='saturate' values='0'/></filter>"
        + "<rect width='100%' height='100%' filter='url(%23g)'/></svg>\")";

    private const string Vignette = "radial-gradient(ellipse at center, rgba(0,0,0,0) 55%, rgba(0,0,0,0.35) 100%)";

    /// <summary>The element of the layer (placed right after the root element), or empty when the token draws nothing.</summary>
    public static string Element(string? texture) => Css(texture).Length == 0 ? string.Empty : "<div id=\"tm-texture\" aria-hidden=\"true\"></div>\n";

    /// <summary>The page style of the layer for <paramref name="texture"/> ("paper", "grain", "scanlines", "halftone", "grid"); empty for none.</summary>
    public static string Css(string? texture)
    {
        var look = texture switch
        {
            "paper" => $"background-image: {PaperNoise}, {Vignette}; background-size: 400px 400px, 100% 100%; opacity: 0.22; mix-blend-mode: multiply;",
            "grain" => $"background-image: {GrainNoise}; background-size: 300px 300px; opacity: 0.09; mix-blend-mode: overlay;",
            "scanlines" => "background-image: repeating-linear-gradient(0deg, rgba(0,0,0,0.22) 0px, rgba(0,0,0,0.22) 1px, rgba(0,0,0,0) 2px, rgba(0,0,0,0) 4px), "
                + $"{Vignette}; opacity: 0.8;",
            "halftone" => "background-image: radial-gradient(rgba(0,0,0,0.5) 22%, rgba(0,0,0,0) 24%); background-size: 7px 7px; opacity: 0.12; "
                + "mix-blend-mode: multiply;",
            "grid" => "background-image: linear-gradient(var(--tm-text, #ffffff) 1px, transparent 1px), linear-gradient(90deg, var(--tm-text, #ffffff) 1px, "
                + "transparent 1px); background-size: calc(var(--tm-unit, 1080px) / 18) calc(var(--tm-unit, 1080px) / 18); opacity: 0.08;",
            _ => string.Empty,
        };
        return look.Length == 0
            ? string.Empty
            : "#tm-texture { position: absolute; left: 0; top: 0; width: 100%; height: 100%; pointer-events: none; z-index: 2147483000; " + look + " }\n";
    }
}
