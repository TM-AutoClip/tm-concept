using System.Globalization;
using System.Text;
using TMConcept.Core.Render;

namespace TMConcept.Core.Subtitles;

/// <summary>
/// Subtitles from the narration: each scene's text is cut into pieces of at most two lines of 42 characters (at sentence ends first,
/// then at words); a piece's time is its share of the scene's spoken length by characters. Writes SRT (always next to the video) and ASS
/// (burned in when on-screen subtitles are chosen).
/// </summary>
public static class SubtitleBuilder
{
    /// <summary>Characters per line.</summary>
    public const int LineMax = 42;

    private static readonly char[] WordSeparators = [' ', '\n', '\r', '\t'];

    /// <summary>Cues of the scenes.</summary>
    public static IReadOnlyList<SubtitleCue> Build(IReadOnlyList<SubtitleSource> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        var cues = new List<SubtitleCue>();
        foreach (var scene in scenes.Where(s => s.Text.Trim().Length > 0 && s.DurationMs > 0))
        {
            var pieces = Pieces(scene.Text);
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

    /// <summary>ASS text sized for the video (white text, dark outline, lower third).</summary>
    public static string Ass(IReadOnlyList<SubtitleCue> cues, int width, int height, RenderTheme theme)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(theme);
        var c = CultureInfo.InvariantCulture;
        var size = (int)Math.Round(Math.Min(width, height) * 0.055);
        var margin = (int)Math.Round(height * 0.12);
        var text = new StringBuilder();
        text.Append("[Script Info]\nScriptType: v4.00+\nWrapStyle: 2\nScaledBorderAndShadow: yes\n")
            .Append(c, $"PlayResX: {width}\nPlayResY: {height}\n\n")
            .Append("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ")
            .Append("ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n")
            .Append(c, $"Style: Default,DejaVu Sans,{size},{Colour(theme.Text)},&H000000FF,&H00000000,&H64000000,1,0,0,0,100,100,0,0,1,{Math.Max(2, size / 12)},0,2,40,40,{margin},1\n\n")
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
    private static IEnumerable<string> Words(string text)
    {
        foreach (var word in text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            for (var i = 0; i < word.Length; i += LineMax)
            {
                yield return word.Substring(i, Math.Min(LineMax, word.Length - i));
            }
        }
    }

    private static List<List<string>> Pieces(string text)
    {
        var words = Words(text);
        var pieces = new List<List<string>>();
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > LineMax)
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
