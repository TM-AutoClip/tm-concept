namespace TMConcept.Core.Styles;

/// <summary>
/// The colour tokens of a palette, by the names the video code sees (input.json <c>style.colors.&lt;token&gt;</c>, CSS
/// <c>var(--tm-&lt;token&gt;)</c>). The model may use only these colours; a colour literal outside the palette is replaced by its nearest token.
/// </summary>
public static class PaletteTokens
{
    /// <summary>Page background.</summary>
    public const string Background = "background";

    /// <summary>Cards, bands and panels on the background.</summary>
    public const string Surface = "surface";

    /// <summary>The main accent.</summary>
    public const string Accent = "accent";

    /// <summary>The second accent.</summary>
    public const string Accent2 = "accent2";

    /// <summary>Text.</summary>
    public const string Text = "text";

    /// <summary>Quiet text and lines.</summary>
    public const string Muted = "muted";

    /// <summary>Prefix of the CSS custom properties (<c>--tm-accent</c>).</summary>
    public const string CssPrefix = "--tm-";

    /// <summary>Every token, in this order.</summary>
    public static readonly IReadOnlyList<string> All = [Background, Surface, Accent, Accent2, Text, Muted];
}
