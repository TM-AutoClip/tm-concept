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
            Font = font.Family,
            FontRegular = VideoFontFiles.Folder + "/" + font.RegularFile,
            FontBold = font.BoldFile is { } bold ? VideoFontFiles.Folder + "/" + bold : string.Empty,
            TitleStyle = Name(style.TitleStyle),
            TitleX = anchor.X,
            TitleY = anchor.Y,
            DrawStyle = Name(style.DrawStyle),
            Motion = Name(style.Motion),
            Subtitles = style.Subtitles,
            ProgressBar = style.ProgressBar,
            SafeMargin = StyleLayout.SafeMargin,
            SafeBottom = style.Subtitles ? StyleLayout.SubtitleBottom : StyleLayout.SafeMargin,
            EndCardSeconds = VideoStyleRules.Closing(style, language) is { Screen: true } ? StyleLayout.EndCardSeconds : 0,
        };
    }

    /// <summary>The token name of a title style (an unknown value reads as plain).</summary>
    public static string Name(TitleStyle value) => value switch
    {
        TitleStyle.Band => "band",
        TitleStyle.Outline => "outline",
        TitleStyle.Underline => "underline",
        TitleStyle.Boxed => "boxed",
        _ => "plain",
    };

    /// <summary>The token name of a drawing style (an unknown value reads as flat).</summary>
    public static string Name(DrawStyle value) => value switch
    {
        DrawStyle.StickFigure => "stick-figure",
        DrawStyle.LineArt => "line-art",
        DrawStyle.PaperCut => "paper-cut",
        DrawStyle.Infographic => "infographic",
        DrawStyle.Neon => "neon",
        _ => "flat",
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
