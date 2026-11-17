namespace TMConcept.Core.Render;

/// <summary>
/// The template's colours as the video code reads them (input.json <c>style.colors</c>; HyperFrames also as CSS <c>var(--tm-&lt;name&gt;)</c>),
/// one #RRGGBB value per <see cref="Styles.PaletteTokens"/> token. The code may use only these colours.
/// </summary>
public sealed class RenderColors
{
    /// <summary>Page background.</summary>
    public string Background { get; set; } = string.Empty;

    /// <summary>Cards, bands and panels.</summary>
    public string Surface { get; set; } = string.Empty;

    /// <summary>The main accent.</summary>
    public string Accent { get; set; } = string.Empty;

    /// <summary>The second accent.</summary>
    public string Accent2 { get; set; } = string.Empty;

    /// <summary>Text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Quiet text and lines.</summary>
    public string Muted { get; set; } = string.Empty;
}
