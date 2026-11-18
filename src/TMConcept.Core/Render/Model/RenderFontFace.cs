namespace TMConcept.Core.Render;

/// <summary>One file of the template's font as the page loads it (input.json <c>style.faces</c>, CSS <c>@font-face</c>).</summary>
public sealed class RenderFontFace
{
    /// <summary>The file, relative to the project (<c>fonts/Tinos-Italic.ttf</c>).</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>CSS weight: "400", "700", or "100 900" for a single-weight font (no fake bold is drawn).</summary>
    public string Weight { get; set; } = string.Empty;

    /// <summary>CSS style: "normal" or "italic".</summary>
    public string Style { get; set; } = string.Empty;

    /// <summary>CSS format: "truetype" or "opentype".</summary>
    public string Format { get; set; } = string.Empty;
}
