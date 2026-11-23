using System.Globalization;
using System.Text;
using TMConcept.Core.Subtitles;

namespace TMConcept.Core.Styles;

/// <summary>
/// What the app itself draws over a video made with a template, as one ASS file burned in at the mux (libass, with the template's font from
/// the copied font folder): the subtitles in the template's font and colours (when on), a thin progress bar along the top edge (only when the
/// template has it on; it grows with the video's time, whatever the engine) and the closing card (a dimmed backdrop, the channel name, the
/// closing sentence and a button in the accent colour) over the last <see cref="StyleLayout.EndCardSeconds"/> seconds: the silent hold the
/// timing added to the last scene for it (<see cref="VideoStyleRules.EndHoldMs"/>), so it covers no narration; a subtitle still on screen then
/// ends where the card starts. The round channel
/// photo is not text: ffmpeg lays it over the card at <see cref="EndCardLayout"/>. Drawn by the app, so the model's code never draws any of
/// it and every engine (and kept code) gets the same result.
/// </summary>
public static class StyleOverlay
{
    /// <summary>Circle drawing: control point distance of a quarter circle (bezier), as a share of the radius.</summary>
    private const double Kappa = 0.5523;

    /// <summary>
    /// The overlay of a video: <paramref name="cues"/> are its subtitles (drawn only when the template has them on), <paramref name="closing"/>
    /// its closing in its language (<see cref="VideoStyleRules.Closing"/>; the card only when <see cref="ClosingText.Screen"/>),
    /// <paramref name="channel"/> the channel's name on the card, <paramref name="photo"/> whether the channel photo is laid over it.
    /// <paramref name="needs"/> (0.1.25, readable text only): what each subtitle needs over the video behind it (<see cref="TextBackdrop"/>),
    /// by cue; a stronger edge or a subtle backdrop where the contrast is low. Missing = the normal readable edge.
    /// </summary>
    public static StyleOverlayResult Build(int width, int height, double durationSeconds, IReadOnlyList<SubtitleCue> cues, VideoStyle style, string language,
        ClosingText? closing, string channel, bool photo, IReadOnlyList<TextBackdropNeed>? needs = null)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 2);
        var palette = VideoStyleRules.Palette(style);
        var font = VideoStyleRules.Font(style, language);
        var subtitles = style.Subtitles && cues.Count > 0;
        var card = closing is { Screen: true } ? Card(width, height, durationSeconds, photo) : null;
        if (!subtitles && !style.ProgressBar && card is null)
        {
            return new StyleOverlayResult(null, null);
        }

        var c = CultureInfo.InvariantCulture;
        var size = SubtitleBuilder.FontSize(width, height);
        var side = SubtitleBuilder.SideMargin(width);
        var bold = font.BoldFile is null ? 0 : 1;
        var subtitleBold = VideoStyleRules.SubtitlesBold(style, font) ? 1 : 0;
        var italic = VideoStyleRules.Italic(style) ? 1 : 0;
        var unit = Math.Min(width, height);
        var text = SubtitleBuilder.AssColour(palette.Text);
        var background = SubtitleBuilder.AssColour(palette.Background);
        var accent = SubtitleBuilder.AssColour(palette.Accent);
        var edge = Edge(style, palette, size);
        var (highlightOn, highlightOff) = WordHighlight.Tags(style, palette, edge);
        var ass = new StringBuilder();
        ass.Append("[Script Info]\nScriptType: v4.00+\nWrapStyle: 0\nScaledBorderAndShadow: yes\n")
            .Append(c, $"PlayResX: {width}\nPlayResY: {height}\n\n")
            .Append("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ")
            .Append("ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n")
            .Append(c, $"Style: Default,{font.Family},{size},{text},&H000000FF,{edge.Outline},{edge.Back},{subtitleBold},{italic},0,0,100,100,0,0,1,{edge.Width},{edge.Shadow},2,{side},{side},{(int)Math.Round(height * 0.12)},1\n")
            .Append(c, $"Style: TmBoxed,{font.Family},{size},{text},&H000000FF,{Backdrop(edge)},{Backdrop(edge)},{subtitleBold},{italic},0,0,100,100,0,0,3,{Math.Max(2, size / 5)},0,2,{side},{side},{(int)Math.Round(height * 0.12)},1\n")
            .Append(c, $"Style: TmShape,{font.Family},10,{text},&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1\n")
            .Append(c, $"Style: TmName,{font.Family},{Name(unit)},{text},&H000000FF,{background},&H00000000,{bold},0,0,0,100,100,0,0,1,{Math.Max(1, Name(unit) / 20)},0,8,{side},{side},0,1\n")
            .Append(c, $"Style: TmPhrase,{font.Family},{Phrase(unit)},{text},&H000000FF,{background},&H00000000,0,0,0,0,100,100,0,0,1,{Math.Max(1, Phrase(unit) / 24)},0,8,{side},{side},0,1\n")
            .Append(c, $"Style: TmButton,{font.Family},{Button(unit)},{background},&H000000FF,{accent},&H00000000,{bold},0,0,0,100,100,0,0,3,{Padding(unit)},0,8,{side},{side},0,1\n\n")
            .Append("[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
        if (subtitles)
        {
            // Nothing is said under the card (it covers the hold after the narration); a cue that would still run then ends where it starts.
            var stop = card is null ? long.MaxValue : (long)Math.Round(card.StartSeconds * 1000);
            for (var i = 0; i < cues.Count; i++)
            {
                var cue = cues[i];
                if (cue.StartMs >= stop)
                {
                    continue;
                }

                var need = style.ReadableText && needs is not null && i < needs.Count ? needs[i] : TextBackdropNeed.Plain;
                var lineStyle = need == TextBackdropNeed.Backdrop ? "TmBoxed" : "Default";
                var prefix = need == TextBackdropNeed.Stronger ? Stronger(edge) : string.Empty;
                if (style.WordHighlight)
                {
                    // 0.1.34: one step per word, the word being said in the accent (its cue's time shared by the words' lengths).
                    foreach (var (from, to, words) in WordHighlight.Steps(cue, highlightOn, highlightOff))
                    {
                        if (from < stop)
                        {
                            Event(ass, 0, from, Math.Min(to, stop), lineStyle, prefix + words);
                        }
                    }

                    continue;
                }

                var line = string.Join("\\N", cue.Lines.Select(Plain));
                Event(ass, 0, cue.StartMs, Math.Min(cue.EndMs, stop), lineStyle, prefix + line);
            }
        }

        var end = (long)Math.Round(durationSeconds * 1000);
        if (style.ProgressBar && end > 0)
        {
            // A dim track and the accent bar over it; the bar is clipped to the time that has passed (libass animates the clip, linear).
            var bar = Math.Max(6, (int)Math.Round(height * StyleLayout.ProgressBarHeight));
            var shape = Rectangle(width, bar);
            Event(ass, 5, 0, end, "TmShape", @"{\an7\pos(0,0)\1c" + Tag(palette.Muted) + @"\1a&H90&\p1}" + shape);
            Event(ass, 6, 0, end, "TmShape", @"{\an7\pos(0,0)\1c" + Tag(palette.Accent) + string.Create(c, $@"\clip(0,0,0,{bar})\t(0,{end},\clip(0,0,{width},{bar}))\p1}}") + shape);
        }

        if (card is not null)
        {
            CardEvents(ass, card, closing!, channel, palette, width, height);
        }

        return new StyleOverlayResult(ass.ToString(), card);
    }

    /// <summary>
    /// The edge of the subtitles. Readable text (<see cref="VideoStyle.ReadableText"/>, the default): a thicker outline in the colour opposite
    /// to the text (black under light text, white under dark text) and a soft shadow of it, so a subtitle reads over an image of its own
    /// colour. Off: the look before 0.1.24 (a thin outline in the background colour, no shadow).
    /// </summary>
    public static SubtitleEdge Edge(VideoStyle style, VideoPalette palette, int size)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(palette);
        if (!style.ReadableText)
        {
            return new SubtitleEdge(SubtitleBuilder.AssColour(palette.Background), "&H64000000", Math.Max(2, size / 12), 0);
        }

        var opposite = SubtitleBuilder.AssColour(PaletteColors.Opposite(palette.Text));

        // The shadow: the same colour, about 40 % see-through (ASS alpha &H66).
        return new SubtitleEdge(opposite, "&H66" + opposite[4..], Math.Max(3, (int)Math.Round(size / 8.0)), Math.Max(2, (int)Math.Round(size / 22.0)));
    }

    /// <summary>
    /// The override of a subtitle over a background with too little contrast (<see cref="TextBackdropNeed.Stronger"/>): the outline half as
    /// thick again, a longer shadow, and the shadow only about 20 % see-through (ASS alpha &amp;H33).
    /// </summary>
    public static string Stronger(SubtitleEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        var width = (int)Math.Round(edge.Width * 1.5, MidpointRounding.AwayFromZero);
        var shadow = Math.Max(2, (int)Math.Round(edge.Shadow * 1.5, MidpointRounding.AwayFromZero));
        return string.Create(CultureInfo.InvariantCulture, $@"{{\bord{width}\shad{shadow}\4a&H33&}}");
    }

    /// <summary>
    /// The box colour of a subtitle over a busy or same-brightness background (<see cref="TextBackdropNeed.Backdrop"/>): the outline's colour,
    /// about 35 % see-through (ASS alpha &amp;H59), so the picture still shows through: a subtle backdrop, not a solid bar.
    /// </summary>
    public static string Backdrop(SubtitleEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return "&H59" + edge.Outline[4..];
    }

    /// <summary>
    /// When and where the closing card goes (the photo's place; the texts follow it in <see cref="Build"/>): the last
    /// <see cref="StyleLayout.EndCardSeconds"/> seconds, the hold the timing added for it.
    /// </summary>
    public static EndCardLayout Card(int width, int height, double durationSeconds, bool photo)
    {
        var unit = Math.Min(width, height);
        var start = Math.Max(0, durationSeconds - StyleLayout.EndCardSeconds);
        var size = PhotoSize(unit);
        var top = Top(width, height, photo);
        return new EndCardLayout(Math.Round(start, 3), durationSeconds, photo, (width / 2) - (size / 2), top, size);
    }

    /// <summary>Text for an ASS line: braces and backslashes cannot start an override, line ends become spaces.</summary>
    public static string Plain(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("{", "(", StringComparison.Ordinal).Replace("}", ")", StringComparison.Ordinal).Replace("\\", "/", StringComparison.Ordinal)
            .ReplaceLineEndings(" ");
    }

    /// <summary>An override tag colour (<c>&amp;HBBGGRR&amp;</c>) of a <c>#rrggbb</c> value.</summary>
    public static string Tag(string hex)
    {
        var colour = SubtitleBuilder.AssColour(hex);
        return "&H" + colour[4..] + "&";
    }

    private static void CardEvents(StringBuilder ass, EndCardLayout card, ClosingText closing, string channel, VideoPalette palette, int width, int height)
    {
        var c = CultureInfo.InvariantCulture;
        var unit = Math.Min(width, height);
        var start = (long)Math.Round(card.StartSeconds * 1000);
        var end = (long)Math.Round(card.EndSeconds * 1000);
        var fade = string.Create(c, $@"\fad({(int)Math.Round(StyleLayout.EndCardFadeSeconds * 1000)},0)");
        var centre = width / 2;
        var y = card.PhotoY;

        // The backdrop: the background colour over the whole picture, mostly opaque, so the card reads over any scene.
        Event(ass, 10, start, end, "TmShape", @"{\an7\pos(0,0)" + fade + @"\1c" + Tag(palette.Background) + @"\1a&H38&\p1}" + Rectangle(width, height));
        if (card.Photo)
        {
            // An accent ring behind the photo (ffmpeg lays the round photo over its middle).
            var radius = (card.PhotoSize / 2) + Ring(unit);
            var left = centre - radius;
            var top = y + (card.PhotoSize / 2) - radius;
            Event(ass, 11, start, end, "TmShape", string.Create(c, $@"{{\an7\pos({left},{top})") + fade + @"\1c" + Tag(palette.Accent) + @"\p1}" + Circle(radius));
            y += card.PhotoSize + (int)Math.Round(unit * 0.04);
        }

        var name = Plain(channel).Trim();
        if (name.Length > 0)
        {
            Event(ass, 12, start, end, "TmName", string.Create(c, $@"{{\pos({centre},{y})") + fade + "}" + name);
            y += Name(unit) + (int)Math.Round(unit * 0.025);
        }

        var sentence = Plain(closing.Sentence).Trim();
        Event(ass, 12, start, end, "TmPhrase", string.Create(c, $@"{{\pos({centre},{y})") + fade + "}" + sentence);
        y += (int)Math.Round(PhraseLines(sentence, width, unit) * Phrase(unit) * 1.25) + (int)Math.Round(unit * 0.05) + Padding(unit);
        Event(ass, 13, start, end, "TmButton", string.Create(c, $@"{{\pos({centre},{y})") + fade + "}" + Plain(closing.Label).Trim());
    }

    private static void Event(StringBuilder ass, int layer, long startMs, long endMs, string style, string text) =>
        ass.Append("Dialogue: ").Append(layer.ToString(CultureInfo.InvariantCulture)).Append(',').Append(SubtitleBuilder.AssTime(startMs)).Append(',')
            .Append(SubtitleBuilder.AssTime(Math.Max(startMs + 10, endMs))).Append(',').Append(style).Append(",,0,0,0,,").Append(text).Append('\n');

    /// <summary>The card's top edge: the block (photo, name, two lines of sentence, button) centred a little above the middle.</summary>
    private static int Top(int width, int height, bool photo)
    {
        var unit = Math.Min(width, height);
        var block = (photo ? PhotoSize(unit) + (int)Math.Round(unit * 0.04) : 0) + Name(unit) + (int)Math.Round(unit * 0.025)
            + (int)Math.Round(2 * Phrase(unit) * 1.25) + (int)Math.Round(unit * 0.05) + Button(unit) + (2 * Padding(unit));
        return Math.Max(0, (int)Math.Round((height * 0.46) - (block / 2.0)));
    }

    private static int PhraseLines(string sentence, int width, int unit)
    {
        var usable = width - (2 * SubtitleBuilder.SideMargin(width));
        var perLine = Math.Max(8, (int)Math.Floor(usable / (Phrase(unit) * 0.55)));
        return Math.Clamp((int)Math.Ceiling(sentence.Length / (double)perLine), 1, 3);
    }

    private static int PhotoSize(int unit) => Math.Max(2, (int)Math.Round(unit * 0.30 / 2) * 2);

    private static int Name(int unit) => Math.Max(8, (int)Math.Round(unit * 0.065));

    private static int Phrase(int unit) => Math.Max(8, (int)Math.Round(unit * 0.05));

    private static int Button(int unit) => Math.Max(8, (int)Math.Round(unit * 0.045));

    private static int Padding(int unit) => Math.Max(4, (int)Math.Round(unit * 0.02));

    private static int Ring(int unit) => Math.Max(4, (int)Math.Round(unit * 0.008));

    private static string Rectangle(int width, int height) => string.Create(CultureInfo.InvariantCulture, $"m 0 0 l {width} 0 {width} {height} 0 {height}");

    /// <summary>A circle of <paramref name="radius"/> drawn from (0, 0) to (2r, 2r): four bezier quarters.</summary>
    private static string Circle(int radius)
    {
        var c = CultureInfo.InvariantCulture;
        var r = radius;
        var d = 2 * r;
        var k = (int)Math.Round(r * Kappa);
        return string.Create(c,
            $"m {r} 0 b {r + k} 0 {d} {r - k} {d} {r} b {d} {r + k} {r + k} {d} {r} {d} b {r - k} {d} 0 {r + k} 0 {r} b 0 {r - k} {r - k} 0 {r} 0");
    }
}
