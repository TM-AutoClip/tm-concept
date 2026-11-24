using System.Globalization;
using TMConcept.Core.Styles;

namespace TMConcept.Core.Render;

/// <summary>
/// Puts a video's template into its render input: the old three-colour <see cref="RenderInput.Theme"/> from the palette (kept code that reads
/// only <c>theme</c> follows the template too) and <see cref="RenderInput.Style"/>, the resolved tokens the code and the app's helpers read. A
/// video without a template (made before templates) is left exactly as it is.
/// </summary>
public static class RenderStyleBuilder
{
    /// <summary>Applies <paramref name="style"/> to <paramref name="input"/> (nothing when it is <c>null</c>).</summary>
    public static void Apply(RenderInput input, VideoStyle? style)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (style is null)
        {
            return;
        }

        input.Theme = VideoStyleRules.Theme(VideoStyleRules.Palette(style));
        input.Style = Tokens(style, input.Language);
        input.Style.Finish = RenderFinishBuilder.For(style, input.Title);
    }

    /// <summary>The tokens of a template for a video language (the font may fall back for Cyrillic, the closing card depends on the language).</summary>
    public static RenderStyle Tokens(VideoStyle style, string language)
    {
        ArgumentNullException.ThrowIfNull(style);
        var palette = VideoStyleRules.Palette(style);
        var font = VideoStyleRules.Font(style, language);
        var anchor = TitleAnchors.Of(Enum.IsDefined(style.TitlePosition) ? style.TitlePosition : TitlePosition.TopCenter);
        return new RenderStyle
        {
            Colors = new RenderColors
            {
                Background = palette.Background,
                Surface = palette.Surface,
                Accent = palette.Accent,
                Accent2 = palette.Accent2,
                Text = palette.Text,
                Muted = palette.Muted,
            },
            Outline = style.ReadableText ? PaletteColors.Opposite(palette.Text) : string.Empty,
            Font = font.Family,
            FontRegular = FilePath(font.RegularFile),
            FontBold = font.BoldFile is { } bold ? FilePath(bold) : string.Empty,
            Faces = Faces(font),
            TextWeight = VideoStyleRules.TextWeight(style),
            TitleWeight = VideoStyleRules.TitleWeight(style),
            TextStyle = VideoStyleRules.Italic(style) ? "italic" : "normal",
            TitleStyle = Name(style.TitleStyle),
            TitleX = anchor.X,
            TitleY = anchor.Y,
            DrawStyle = Name(style.DrawStyle),
            Texture = Texture(DrawStyles.Of(style.DrawStyle).Texture),
            Motion = Name(style.Motion),
            Subtitles = style.Subtitles,
            ProgressBar = style.ProgressBar,
            SafeMargin = StyleLayout.SafeMargin,
            SafeBottom = style.Subtitles ? StyleLayout.SubtitleBottom : StyleLayout.SafeMargin,
            EndCardSeconds = VideoStyleRules.Closing(style, language) is { Screen: true } ? StyleLayout.EndCardSeconds : 0,
        };
    }

    /// <summary>
    /// The faces of a font for the page: regular and bold (a single-weight font claims every weight, so no fake bold is drawn), italic and
    /// bold italic when the font has them (without them the browser slants the upright face).
    /// </summary>
    public static List<RenderFontFace> Faces(VideoFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        var single = font.BoldFile is null;
        var faces = new List<RenderFontFace> { Face(font.RegularFile, single ? "100 900" : "400", "normal") };
        if (font.BoldFile is { } bold)
        {
            faces.Add(Face(bold, "700", "normal"));
        }

        if (font.ItalicFile is { } italic)
        {
            faces.Add(Face(italic, single || font.BoldItalicFile is null ? "100 900" : "400", "italic"));
        }

        if (font.BoldItalicFile is { } boldItalic)
        {
            faces.Add(Face(boldItalic, "700", "italic"));
        }

        return faces;
    }

    /// <summary>
    /// The faces the page loads: <see cref="RenderStyle.Faces"/>, or for a style written before 0.1.24 (kept code rendered again) its regular
    /// and bold files as they were loaded then.
    /// </summary>
    public static IReadOnlyList<RenderFontFace> FacesOf(RenderStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (style.Faces.Count > 0)
        {
            return style.Faces;
        }

        var single = style.FontBold.Length == 0;
        var faces = new List<RenderFontFace> { new() { File = style.FontRegular, Weight = single ? "100 900" : "400", Style = "normal", Format = "truetype" } };
        if (!single)
        {
            faces.Add(new RenderFontFace { File = style.FontBold, Weight = "700", Style = "normal", Format = "truetype" });
        }

        return faces;
    }

    private static RenderFontFace Face(string file, string weight, string style) =>
        new() { File = FilePath(file), Weight = weight, Style = style, Format = VideoFontFiles.Format(file) };

    /// <summary>A font file's path inside the project (an installed font's full path loses its folder).</summary>
    private static string FilePath(string file) => VideoFontFiles.Folder + "/" + VideoFontFiles.Name(file);

    /// <summary>The token name of a title style (an unknown value reads as plain).</summary>
    public static string Name(TitleStyle value) => value switch
    {
        TitleStyle.Band => "band",
        TitleStyle.Outline => "outline",
        TitleStyle.Underline => "underline",
        TitleStyle.Boxed => "boxed",
        _ => "plain",
    };

    /// <summary>The token name of a drawing style (<see cref="DrawStyleDefinition.Key"/>; an unknown value reads as flat).</summary>
    public static string Name(DrawStyle value) => DrawStyles.Of(value).Key;

    /// <summary>The token of a texture ("paper", "grain", "scanlines", "halftone", "grid"; empty for none).</summary>
    public static string Texture(StyleTexture value) => value switch
    {
        StyleTexture.Paper => "paper",
        StyleTexture.Grain => "grain",
        StyleTexture.Scanlines => "scanlines",
        StyleTexture.Halftone => "halftone",
        StyleTexture.Grid => "grid",
        _ => string.Empty,
    };

    /// <summary>The token name of a motion intensity (an unknown value reads as normal).</summary>
    public static string Name(MotionIntensity value) => value switch
    {
        MotionIntensity.Calm => "calm",
        MotionIntensity.Lively => "lively",
        _ => "normal",
    };

    /// <summary>A position in words for the prompt ("top centre").</summary>
    public static string Words(TitlePosition position)
    {
        var anchor = TitleAnchors.Of(Enum.IsDefined(position) ? position : TitlePosition.TopCenter);
        var row = anchor.Row switch { 0 => "top", 1 => "middle", _ => "bottom" };
        var column = anchor.Column switch { 0 => "left", 1 => "centre", _ => "right" };
        return string.Create(CultureInfo.InvariantCulture, $"{row} {column}");
    }
}
