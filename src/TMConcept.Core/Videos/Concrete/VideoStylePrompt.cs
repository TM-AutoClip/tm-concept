using System.Globalization;
using System.Text;
using TMConcept.Core.Engines;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;

namespace TMConcept.Core.Videos;

/// <summary>
/// The compact "Template" block of the video code prompt (v9) and the drawing style of the image prompts. Kept short on purpose (about
/// 300–450 tokens; a local model may have only 8 192 tokens of context and 4 096 of output): the app enforces the template itself (it draws the
/// progress bar, the subtitles and the closing card, copies the font, checks the code and replaces colours outside the palette), so the block
/// only says what the code must do and which ready helpers save it from writing layout code.
/// </summary>
public static class VideoStylePrompt
{
    /// <summary>The block for <paramref name="style"/> on <paramref name="engine"/> in the video's <paramref name="language"/>.</summary>
    public static string Block(EngineKind engine, VideoStyle style, string language)
    {
        ArgumentNullException.ThrowIfNull(style);
        var tokens = RenderStyleBuilder.Tokens(style, language);
        var c = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("Template (the user's choice; the app checks the code: a break costs a repair or fails the video):\n")
            .Append("- Colours: ONLY input.style.colors (background, surface, accent, accent2, text, muted)")
            .Append(engine == EngineKind.HyperFrames ? " or CSS var(--tm-<name>)" : string.Empty)
            .Append(". Write no other colour value: any other is replaced by the nearest of these.\n")
            .Append(c, $"- Font: ONLY input.style.font (\"{tokens.Font}\"), weight {tokens.TextWeight} (titles {tokens.TitleWeight}), {tokens.TextStyle}; it is already loaded")
            .Append(engine == EngineKind.HyperFrames ? " and is the page's font (var(--tm-font))" : string.Empty)
            .Append(". Never name another font family.\n")
            .Append(Readable(engine, tokens))
            .Append(c, $"- Titles: {tokens.TitleStyle} ({TitleWords(style.TitleStyle)}), at the {RenderStyleBuilder.Words(style.TitlePosition)} of the safe area")
            .Append(" (input.style.titleX / titleY, 0–1).\n")
            .Append(c, $"- Scenes without an image are drawn as {DrawWords(style.DrawStyle)}.\n")
            .Append(c, $"- Motion: {tokens.Motion}, {MotionWords(style.Motion)}.\n")
            .Append(c, $"- Safe area: {StyleLayout.SafeMargin * 100:0} % margin on every side")
            .Append(style.Subtitles ? string.Create(c, $"; the bottom {StyleLayout.SubtitleBottom * 100:0} % stays free for the subtitles.\n") : ".\n")
            .Append("- NEVER draw a progress bar, timeline bar, timer, countdown or scene counter (\"2/7\")");
        var drawn = new List<string>();
        if (style.ProgressBar)
        {
            drawn.Add("the progress bar along the top edge");
        }

        if (style.Subtitles)
        {
            drawn.Add("the subtitles");
        }

        if (tokens.EndCardSeconds > 0)
        {
            drawn.Add(string.Create(c, $"a closing card over the last {tokens.EndCardSeconds:0.#} s (start nothing new there)"));
        }

        text.Append(drawn.Count > 0 ? "; the app itself draws " + string.Join(", ", drawn) + ".\n" : ".\n");

        return text.Append(Helpers(engine)).ToString().TrimEnd();
    }

    /// <summary>
    /// The readable-text line (the template's default since 0.1.24): HTML engines get the outline from the app's page style, Revideo draws
    /// on a canvas, so its code spreads the helper's props into every text. Empty when the template has it off.
    /// </summary>
    private static string Readable(EngineKind engine, RenderStyle tokens) => tokens.Outline.Length == 0
        ? string.Empty
        : engine == EngineKind.Revideo
            ? "- Readable text: spread tmOutline(input) into every Txt (an outline in the colour opposite to the text).\n"
            : "- Readable text: the app gives every text an outline and shadow in the colour opposite to the text; never set text-shadow yourself.\n";

    /// <summary>The ready helpers of an engine (app-owned files the code may use; they read input.style).</summary>
    public static string Helpers(EngineKind engine) => engine switch
    {
        EngineKind.HyperFrames => """
            - Helpers (window.TM, loaded before your file): TM.color(name) gives a colour; TM.font the font family; TM.stage() the root element;
            TM.title(text) a title element already styled and placed (append it to a scene element, then animate it); TM.figure(pose) an SVG
            stick figure (pose: stand, walk, point, cheer, think; set its style left/top/height as fractions). Prefer them to hand-written layout.
            """,
        EngineKind.Remotion => """
            - Helpers: `import {tmColor, tmFont, TmTitle} from './tm';` tmColor(input, name) gives a colour, tmFont(input) the font family,
            <TmTitle input={input} text={…} /> a title already styled and placed (wrap it in an element to animate it).
            """,
        EngineKind.Revideo => """
            - Helpers: `import {tmColor, tmFont, tmOutline} from './tm';` tmColor(input, name) gives a colour, tmFont(input) the font family (Txt fontFamily),
            tmOutline(input) the readable-text props of a Txt.
            """,
        _ => string.Empty,
    };

    /// <summary>The image prompt's drawing style, so the images match the code's drawings.</summary>
    public static string ImageStyle(DrawStyle style) => style switch
    {
        DrawStyle.StickFigure => "minimal stick-figure illustration, simple line characters on a plain background",
        DrawStyle.LineArt => "clean line art, thin ink lines, minimal shading",
        DrawStyle.PaperCut => "layered paper cut-out illustration, soft shadows, craft paper texture",
        DrawStyle.Infographic => "clean infographic-style illustration with simple icons and diagrams (no text)",
        DrawStyle.Neon => "neon glow illustration, glowing lines on a dark background",
        _ => "flat vector illustration, solid colours, simple shapes",
    };

    private static string TitleWords(TitleStyle style) => style switch
    {
        TitleStyle.Band => "on a full-width band in the surface colour",
        TitleStyle.Outline => "text with an outline in the background colour",
        TitleStyle.Underline => "text with a short accent line under it",
        TitleStyle.Boxed => "text in a rounded box in the accent colour, the text in the background colour",
        _ => "plain text in the text colour",
    };

    private static string DrawWords(DrawStyle style) => style switch
    {
        DrawStyle.StickFigure => "simple stick figures (round head, line body) acting out the idea",
        DrawStyle.LineArt => "thin one-colour line drawings without fills",
        DrawStyle.PaperCut => "layered paper cut-out shapes with soft shadows",
        DrawStyle.Infographic => "an infographic: big numbers, simple charts, icons and arrows",
        DrawStyle.Neon => "glowing accent lines on the dark background (a soft glow, no fills)",
        _ => "flat shapes and simple icons in solid colours",
    };

    private static string MotionWords(MotionIntensity motion) => motion switch
    {
        MotionIntensity.Calm => "few, slow movements with gentle easing (about 1.5× longer animations)",
        MotionIntensity.Lively => "more movement and quicker entrances (about 0.7× the usual length), a little overshoot",
        _ => "a steady, unhurried pace",
    };
}
