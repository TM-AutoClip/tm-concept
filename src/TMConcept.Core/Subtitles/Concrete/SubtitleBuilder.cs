using System.Globalization;
using System.Text;
using TMConcept.Core.Render;

namespace TMConcept.Core.Subtitles;

/// <summary>
/// Subtitles from the narration: each scene's text is cut into pieces of at most two lines (at sentence ends first, then at words); a
/// piece's time is its share of the scene's spoken length by characters. Writes SRT (always next to the video) and ASS (burned in when
/// on-screen subtitles are chosen). The line length comes from the picture (<see cref="LineLength"/>): 0.1.7–0.1.14 used 42 characters at
/// a text size made for wide video, so in a 1080-wide vertical video a long line ran off both sides of the picture (13 Nov 2026).
/// </summary>
public static class SubtitleBuilder
{
    /// <summary>Most characters per line (wide video).</summary>
    public const int LineMax = 42;

    /// <summary>Fewest characters per line, however narrow the picture.</summary>
    public const int LineMin = 16;

    /// <summary>
    /// Width of one character as a share of the text size, taken generously (bold sans text averages about 0.55; capitals and wide letters
    /// run more), so a full line stays inside the picture.
    /// </summary>
    public const double CharWidth = 0.62;

    /// <summary>Text size in pixels for a picture: 5.5 % of its shorter side.</summary>
    public static int FontSize(int width, int height) => Math.Max(1, (int)Math.Round(Math.Min(width, height) * 0.055));

    /// <summary>Left and right margin in pixels: 5 % of the width.</summary>
    public static int SideMargin(int width) => (int)Math.Round(width * 0.05);

    /// <summary>
    /// Characters per line that fit between the side margins at <see cref="FontSize"/> (1080×1920: 26; 1920×1080: 42), between
    /// <see cref="LineMin"/> and <see cref="LineMax"/>.
    /// </summary>
    public static int LineLength(int width, int height)
    {
        var usable = width - (2 * SideMargin(width));
        var fits = (int)Math.Floor(usable / (FontSize(width, height) * CharWidth));
        return Math.Clamp(fits, LineMin, LineMax);
    }

    private static readonly char[] WordSeparators = [' ', '\n', '\r', '\t'];

    /// <summary>Cues of the scenes, lines of at most <paramref name="lineLength"/> characters (<see cref="LineLength"/> for the picture).</summary>
    public static IReadOnlyList<SubtitleCue> Build(IReadOnlyList<SubtitleSource> scenes, int lineLength = LineMax)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineLength, 1);
        var cues = new List<SubtitleCue>();
        foreach (var scene in scenes.Where(s => s.Text.Trim().Length > 0 && s.DurationMs > 0))
        {
            var pieces = Pieces(scene.Text, lineLength);
            var total = pieces.Sum(p => p.Sum(l => l.Length + 1));
            var start = scene.StartMs;
            foreach (var piece in pieces)
            {
                var share = piece.Sum(l => l.Length + 1) / (double)total;
                var end = Math.Min(scene.StartMs + scene.DurationMs, start + (long)Math.Round(scene.DurationMs * share));
                cues.Add(new SubtitleCue(start, Math.Max(start + 1, end), piece));
                start = end;
            }
        }

        return cues;
    }

    /// <summary>SRT text.</summary>
    public static string Srt(IReadOnlyList<SubtitleCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        var text = new StringBuilder();
        for (var i = 0; i < cues.Count; i++)
        {
            text.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append(Time(cues[i].StartMs, ',')).Append(" --> ").Append(Time(cues[i].EndMs, ',')).Append('\n')
                .Append(string.Join('\n', cues[i].Lines)).Append("\n\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// ASS text sized for the video (white text, dark outline, lower third). Smart wrapping stays on (WrapStyle 0): a line that is still too
    /// wide for the picture is broken by libass instead of running off the sides.
    /// </summary>
    public static string Ass(IReadOnlyList<SubtitleCue> cues, int width, int height, RenderTheme theme)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(theme);
        var c = CultureInfo.InvariantCulture;
        var size = FontSize(width, height);
        var margin = (int)Math.Round(height * 0.12);
        var side = SideMargin(width);
        var text = new StringBuilder();
        text.Append("[Script Info]\nScriptType: v4.00+\nWrapStyle: 0\nScaledBorderAndShadow: yes\n")
            .Append(c, $"PlayResX: {width}\nPlayResY: {height}\n\n")
            .Append("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ")
            .Append("ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n")
            .Append(c, $"Style: Default,DejaVu Sans,{size},{Colour(theme.Text)},&H000000FF,&H00000000,&H64000000,1,0,0,0,100,100,0,0,1,{Math.Max(2, size / 12)},0,2,{side},{side},{margin},1\n\n")
            .Append("[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
        foreach (var cue in cues)
        {
            var line = string.Join("\\N", cue.Lines.Select(l => l.Replace("{", "(", StringComparison.Ordinal).Replace("}", ")", StringComparison.Ordinal)));
            text.Append("Dialogue: 0,").Append(Time(cue.StartMs, '.', centiseconds: true)).Append(',').Append(Time(cue.EndMs, '.', centiseconds: true))
                .Append(",Default,,0,0,0,,").Append(line).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Whether a word ends a sentence: ".", "!", "?", "…" (also before a closing quote or bracket: «… dedi.»", "(1453).").</summary>
    public static bool EndsSentence(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var end = word.TrimEnd('"', '\'', '»', '«', '”', '“', '’', ')', ']', '›');
        return end.Length > 0 && end[^1] is '.' or '!' or '?' or '…';
    }

    /// <summary>
    /// The words of a text (split at spaces, tabs and line ends; a no-break space, as in French "Bonjour !", stays inside its word so a line
    /// never starts with the "!"), a word longer than a line cut into line-long parts, so nothing of it is lost.
    /// </summary>
    private static IEnumerable<string> Words(string text, int lineLength)
    {
        foreach (var word in text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            for (var i = 0; i < word.Length; i += lineLength)
            {
                yield return word.Substring(i, Math.Min(lineLength, word.Length - i));
            }
        }
    }

    private static List<List<string>> Pieces(string text, int lineLength)
    {
        var words = Words(text, lineLength);
        var pieces = new List<List<string>>();
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > lineLength)
            {
                lines.Add(line.ToString());
                line.Clear();
                if (lines.Count == 2)
                {
                    pieces.Add(lines);
                    lines = [];
                }
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
            if (EndsSentence(word))
            {
                lines.Add(line.ToString());
                line.Clear();
                pieces.Add(lines);
                lines = [];
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }

        if (lines.Count > 0)
        {
            pieces.Add(lines);
        }

        return pieces;
    }

    private static string Time(long ms, char separator, bool centiseconds = false)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return centiseconds
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}{separator}{t.Milliseconds / 10:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{separator}{t.Milliseconds:000}");
    }

    private static string Colour(string hex)
    {
        var value = hex.TrimStart('#');
        return value.Length == 6 ? "&H00" + value[4..6] + value[2..4] + value[..2] : "&H00FFFFFF";
    }
}
