using System.Globalization;
using System.Text;
using TMConcept.Core.Subtitles;

namespace TMConcept.Core.Styles;

/// <summary>
/// Word-by-word highlighted subtitles (0.1.34, the template's <see cref="VideoStyle.WordHighlight"/>): a subtitle is shown as one step per
/// word, the word being said in the palette's accent and the rest in the text colour, so the eye follows the voice. A cue whose words were
/// heard (0.1.48, F5: <see cref="SubtitleCue.Words"/>) moves the highlight on their real times; any other cue shares its time by the words'
/// length (letters + 1). The highlight follows the drawing style: neon, hologram and terminal glow in the accent, the bold cartoon looks grow
/// the word a little (<see cref="Grow"/>; the line length leaves room for it, <see cref="GrowPercent"/>), the drawn and paper looks only
/// change its colour.
/// </summary>
public static class WordHighlight
{
    /// <summary>How much a highlighted word grows in the bold looks (per cent).</summary>
    public const int Grow = 112;

    /// <summary>
    /// How much the widest word of a subtitle line grows on screen with <paramref name="style"/> (per cent): <see cref="Grow"/> when the
    /// highlight is on and the drawing style grows the word, else 100. The subtitles' line length is divided by it
    /// (<see cref="SubtitleBuilder.LineLength"/>), so the grown word never runs off the picture.
    /// </summary>
    public static int GrowPercent(VideoStyle? style) =>
        style is { Subtitles: true, WordHighlight: true } && Grows(style.DrawStyle) ? Grow : 100;

    /// <summary>Whether the highlight of <paramref name="drawStyle"/> grows the word (the bold cartoon looks).</summary>
    public static bool Grows(DrawStyle drawStyle) =>
        drawStyle is DrawStyle.Flat or DrawStyle.Cartoon or DrawStyle.StickFigure or DrawStyle.Infographic or DrawStyle.Auto;

    /// <summary>
    /// The steps of <paramref name="cue"/>: each word's time range and the cue's text with that word highlighted (<paramref name="on"/> before
    /// it, <paramref name="off"/> after it). A cue without words is one step with its plain text.
    /// </summary>
    public static IReadOnlyList<(long StartMs, long EndMs, string Text)> Steps(SubtitleCue cue, string on, string off)
    {
        ArgumentNullException.ThrowIfNull(cue);
        var lines = cue.Lines.Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var words = lines.SelectMany(l => l).ToList();
        if (words.Count == 0)
        {
            return [(cue.StartMs, cue.EndMs, string.Join("\\N", cue.Lines.Select(StyleOverlay.Plain)))];
        }

        if (cue.Words.Count == words.Count)
        {
            // Heard times: a word is lit from its start to the next word's start (the first from the cue's start, the last to its end).
            var heard = new List<(long, long, string)>(words.Count);
            for (var k = 0; k < words.Count; k++)
            {
                var start = k == 0 ? cue.StartMs : Math.Clamp(cue.Words[k].StartMs, cue.StartMs, cue.EndMs);
                var end = k == words.Count - 1 ? cue.EndMs : Math.Clamp(cue.Words[k + 1].StartMs, start, cue.EndMs);
                heard.Add((start, end, Text(lines, k, on, off)));
            }

            return heard;
        }

        var weights = words.Select(w => w.Count(char.IsLetterOrDigit) + 1.0).ToList();
        var total = weights.Sum();
        var length = Math.Max(1, cue.EndMs - cue.StartMs);
        var steps = new List<(long, long, string)>();
        var at = 0.0;
        for (var k = 0; k < words.Count; k++)
        {
            var start = cue.StartMs + (long)Math.Round(length * at / total);
            at += weights[k];
            var end = k == words.Count - 1 ? cue.EndMs : cue.StartMs + (long)Math.Round(length * at / total);
            steps.Add((start, end, Text(lines, k, on, off)));
        }

        return steps;
    }

    /// <summary>The override tags that turn the highlight on and off for <paramref name="style"/> (<paramref name="palette"/>'s accent and text).</summary>
    public static (string On, string Off) Tags(VideoStyle style, VideoPalette palette, SubtitleEdge edge)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(edge);
        var c = CultureInfo.InvariantCulture;
        var accent = StyleOverlay.Tag(palette.Accent);
        var text = StyleOverlay.Tag(palette.Text);
        var outline = "&H" + edge.Outline[4..] + "&";
        return style.DrawStyle switch
        {
            DrawStyle.Neon or DrawStyle.Hologram or DrawStyle.Terminal => ($@"{{\1c{accent}\3c{accent}\blur3}}", $@"{{\1c{text}\3c{outline}\blur0}}"),
            var draw when Grows(draw) =>
                (string.Create(c, $@"{{\1c{accent}\fscx{Grow}\fscy{Grow}}}"), $@"{{\1c{text}\fscx100\fscy100}}"),
            _ => ($@"{{\1c{accent}}}", $@"{{\1c{text}}}"),
        };
    }

    private static string Text(IReadOnlyList<string[]> lines, int highlighted, string on, string off)
    {
        var text = new StringBuilder();
        var index = 0;
        for (var l = 0; l < lines.Count; l++)
        {
            if (l > 0)
            {
                text.Append("\\N");
            }

            for (var w = 0; w < lines[l].Length; w++, index++)
            {
                if (w > 0)
                {
                    text.Append(' ');
                }

                var word = StyleOverlay.Plain(lines[l][w]);
                text.Append(index == highlighted ? on + word + off : word);
            }
        }

        return text.ToString();
    }
}
