using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Core.Styles;

/// <summary>
/// The template check of a video code, run after the safety check (<see cref="Engines.EngineCodeCheck"/>) and before the code is written. Its
/// findings are handled like a safety finding: the code gets the single paid repair, and a code that still breaks the template is not
/// rendered (the video fails). It rejects:
/// <list type="bullet">
/// <item>a progress bar, timer, countdown or scene counter drawn by the code: the app draws the progress bar itself (only when the template
/// has it on), so a drawn one is either a second bar or one the user turned off;</item>
/// <item>with subtitles off, any access to a scene's caption (the engine gets no captions then; code that reads them means to draw the
/// narration);</item>
/// <item>a font family named in the code other than the template's font (and generic families such as sans-serif).</item>
/// </list>
/// Comments are skipped here, unlike the safety check: this is not a security boundary, and "// no progress bar here" must not cost a repair.
/// The rules look for the names and forms such a drawing needs (progressBar, "progress-line", countdown, <c>${i + 1}/${scenes.length}</c>, a
/// width grown over the whole video's length), not for the plain word "progress" (an animation's progress value is ordinary code).
/// A colour literal outside the palette (<c>#rrggbb</c>, <c>#rgb</c>, <c>rgb()</c>, <c>rgba()</c>) is not a finding: it is replaced by the
/// nearest palette colour's value (<see cref="PaletteColors.Nearest"/>; its opacity kept, as <c>rgba()</c>), deterministically and without a
/// paid call. The value, not <c>var(--tm-…)</c>, so it also works in SVG attributes, on a canvas and in a GSAP colour tween.
/// </summary>
public static partial class VideoStyleCheck
{
    private static readonly string[] Generic =
    [
        "sans-serif", "serif", "monospace", "cursive", "fantasy", "system-ui", "ui-sans-serif", "ui-serif", "ui-monospace", "ui-rounded", "emoji",
        "math", "inherit", "initial", "unset", "revert", "revert-layer",
    ];

    /// <summary>
    /// Checks <paramref name="code"/> against <paramref name="style"/> for a video in <paramref name="language"/> (the font may fall back for
    /// Cyrillic, <see cref="VideoStyleRules.Font"/>).
    /// </summary>
    public static VideoStyleCheckResult Check(string code, VideoStyle style, string language)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(style);
        var findings = new List<string>();
        var lines = code.Split('\n');
        var bare = WithoutComments(code).Split('\n');
        var family = VideoStyleRules.Font(style, language).Family;
        var lengths = LengthNames(bare);
        for (var i = 0; i < bare.Length; i++)
        {
            var line = bare[i];
            if (Chrome().IsMatch(line) || Counter(line) || GrowsOverTheVideo(line, lengths))
            {
                findings.Add($"line {i + 1}: progress bar, timer or scene counter drawn by the code (the app draws the progress bar itself; never draw one, a timer or a scene counter) — {Head(lines[i])}");
            }

            if (!style.Subtitles && (CaptionAccess().IsMatch(line) || CaptionKey().IsMatch(line) || CaptionDestructuring().IsMatch(line)))
            {
                findings.Add($"line {i + 1}: the caption (narration) is read but this video has no subtitles: never read or draw it — {Head(lines[i])}");
            }

            foreach (var foreign in ForeignFonts(line, family))
            {
                findings.Add($"line {i + 1}: font \"{foreign}\" is not the template font: use only \"{family}\" (input.style.font) — {Head(lines[i])}");
            }
        }

        var palette = VideoStyleRules.Palette(style);
        var fixes = new List<string>();
        var fixedCode = Colour().Replace(code, match =>
        {
            if (!PaletteColors.TryParse(match.Value, out var color) || PaletteColors.InPalette(palette, color))
            {
                return match.Value;
            }

            var token = PaletteColors.Nearest(palette, color);
            var value = PaletteColors.Value(palette, token);
            var replacement = color.A < 1 && PaletteColors.TryParse(value, out var target)
                ? string.Create(CultureInfo.InvariantCulture, $"rgba({target.R}, {target.G}, {target.B}, {Math.Round(color.A, 3)})")
                : value;
            fixes.Add($"{match.Value} → {replacement} ({token})");
            return replacement;
        });

        return new VideoStyleCheckResult(fixedCode, findings, fixes);
    }

    /// <summary>
    /// The code with its comments blanked out (line ends kept, so line numbers stay), read with a small scanner that knows quoted strings and
    /// template literals. Not a full parser: a regular expression literal with a quote or "//" in it may confuse it, which only matters for this
    /// check's convenience, not for safety.
    /// </summary>
    internal static string WithoutComments(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var text = new StringBuilder(code.Length);
        var i = 0;
        while (i < code.Length)
        {
            var c = code[i];
            var next = i + 1 < code.Length ? code[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < code.Length && code[i] != '\n')
                {
                    text.Append(' ');
                    i++;
                }

                continue;
            }

            if (c == '/' && next == '*')
            {
                var end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = end < 0 ? code.Length : end + 2;
                for (; i < stop; i++)
                {
                    text.Append(code[i] == '\n' ? '\n' : ' ');
                }

                continue;
            }

            if (c is '"' or '\'' or '`')
            {
                i = Quoted(code, i, text);
                continue;
            }

            text.Append(c);
            i++;
        }

        return text.ToString();
    }

    /// <summary>Copies a quoted string or template literal starting at <paramref name="start"/>; returns the index after it.</summary>
    private static int Quoted(string code, int start, StringBuilder text)
    {
        var quote = code[start];
        text.Append(quote);
        var i = start + 1;
        while (i < code.Length && code[i] != quote && (quote == '`' || code[i] != '\n'))
        {
            if (code[i] == '\\' && i + 1 < code.Length)
            {
                text.Append(code[i]).Append(code[i + 1]);
                i += 2;
                continue;
            }

            text.Append(code[i]);
            i++;
        }

        if (i < code.Length && code[i] == quote)
        {
            text.Append(quote);
            i++;
        }

        return i;
    }

    /// <summary>A shown "n / total" of the scenes: the scene count next to a slash or "of" in a text.</summary>
    private static bool Counter(string line) =>
        SceneCount().IsMatch(line) && (QuotedSlash().IsMatch(line) || TemplateSlash().IsMatch(line));

    /// <summary>
    /// A width or horizontal scale set or animated over the whole video (the length of a progress bar), not over a scene. The length is read
    /// from the input (<c>input.durationSeconds</c>, <c>totalFrames</c>), from Remotion's <c>durationInFrames</c> used as a value, or from a
    /// name it was given earlier (<paramref name="lengths"/>: <c>const total = INPUT.durationSeconds;</c>). Reading the video's size
    /// (<c>input.width</c>) is not a growth: a slow background drift over the whole video is ordinary motion.
    /// </summary>
    private static bool GrowsOverTheVideo(string line, IReadOnlyCollection<string> lengths) =>
        Growth().IsMatch(line) && (WholeLength().IsMatch(line) || lengths.Any(name => Regex.IsMatch(line, $@"(?<![\w$.]){Regex.Escape(name)}(?![\w$])")));

    /// <summary>
    /// The names the code gives the whole video's length: <c>const total = input.durationSeconds</c> (or totalFrames, durationInFrames), and
    /// <c>durationSeconds</c> / <c>totalFrames</c> destructured from the input. A name declared on the line only (no growth there) is
    /// judged where it is used.
    /// </summary>
    private static List<string> LengthNames(IEnumerable<string> lines)
    {
        var names = new List<string>();
        foreach (var line in lines)
        {
            names.AddRange(LengthAssignment().Matches(line).Select(m => m.Groups[1].Value));
            foreach (Match match in LengthDestructuring().Matches(line))
            {
                foreach (var part in match.Groups[1].Value.Split(','))
                {
                    // "{durationSeconds}" or "{durationSeconds: total}": the local name is after the colon.
                    var pieces = part.Split(':');
                    if (pieces[0].Trim() is "durationSeconds" or "totalFrames" or "durationInFrames")
                    {
                        names.Add((pieces.Length > 1 ? pieces[1] : pieces[0]).Split('=')[0].Trim());
                    }
                }
            }
        }

        return names.Where(n => n.Length > 0 && n != "durationInFrames").Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The font families a line names that are neither the template's font nor generic (values built in code are not judged).</summary>
    private static List<string> ForeignFonts(string line, string family)
    {
        var lists = new List<string>();
        lists.AddRange(CssFamily().Matches(line).Select(m => m.Groups[1].Value));
        lists.AddRange(JsFamily().Matches(line).Select(m => m.Groups[2].Value));
        foreach (var value in CssShorthand().Matches(line).Select(m => m.Groups[1].Value).Concat(JsShorthand().Matches(line).Select(m => m.Groups[2].Value)))
        {
            if (ShorthandFamilies().Match(value) is { Success: true } families)
            {
                lists.Add(families.Groups[1].Value);
            }
        }

        var foreign = new List<string>();
        foreach (var list in lists.Where(l => !l.Contains("${", StringComparison.Ordinal)))
        {
            foreach (var raw in list.Split(','))
            {
                var name = raw.Replace("!important", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().Trim('"', '\'', '`', '\\').Trim();
                if (name.Contains('+', StringComparison.Ordinal) || name.Contains('$', StringComparison.Ordinal) || name.Contains('(', StringComparison.Ordinal))
                {
                    // A value built in code (or a var()): not a literal family.
                    break;
                }

                if (name.Length > 0 && !name.Equals(family, StringComparison.OrdinalIgnoreCase) && !Generic.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    foreign.Add(name);
                }
            }
        }

        return foreign;
    }

    private static string Head(string line)
    {
        var text = line.Trim();
        return text.Length <= 80 ? text : text[..80] + "…";
    }

    [GeneratedRegex(@"progress[\s_-]*(?:bar|line|track|indicator|fill|ring|dots?)|\bscrubber|\bseek[\s_-]*bar|\bcount[\s_-]*down|\btimer(?:s|el|text|label|box)?\b|\btime[\s_-]*(?:left|remaining)\b|\bremaining[\s_-]*time\b|\belapsed[\s_-]*(?:time[\s_-]*)?(?:label|text)\b|\bscene[\s_-]*(?:counter|indicator|badge|count[\s_-]*label|index[\s_-]*label|number[\s_-]*label)\b|\b(?:page|step|slide)[\s_-]*(?:counter|indicator)\b|\bdots?[\s_-]*indicator\b", RegexOptions.IgnoreCase)]
    private static partial Regex Chrome();

    [GeneratedRegex(@"\bscenes\s*\.\s*length\b")]
    private static partial Regex SceneCount();

    [GeneratedRegex(@"['""`]\s*(?:/|of)\s*['""`]?\s*\+|\+\s*['""`]\s*(?:/|of)\s*['""`]")]
    private static partial Regex QuotedSlash();

    [GeneratedRegex(@"\}\s*(?:/|of)\s*\$\{")]
    private static partial Regex TemplateSlash();

    // durationInFrames counts as a value (useVideoConfig's), not as the key or attribute of a Sequence or a composition.
    [GeneratedRegex(@"\b(?:input|INPUT|props|data)\s*\.\s*(?:durationSeconds|totalFrames)\b|\bdurationInFrames\b(?!\s*[:=])")]
    private static partial Regex WholeLength();

    // A size property set or animated ("width:", ".style.width =", "width={", setAttribute("x2", …), Revideo's ".width(…)"), never the
    // video's size read from the input (input.width) or a Revideo getter (view.width()).
    [GeneratedRegex(@"(?<!\b(?:input|INPUT|props|data|config|videoConfig)\s*\.\s*)(?<![\w$-])(?:width|scaleX|x2|strokeDashoffset)['""]?\s*(?:[:,]|\((?!\s*\))|=(?!=))|scale-x")]
    private static partial Regex Growth();

    [GeneratedRegex(@"\b(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=\s*(?:(?:input|INPUT|props|data)\s*\.\s*(?:durationSeconds|totalFrames)\b|(?:useVideoConfig\s*\(\s*\)\s*\.\s*)?durationInFrames\b)")]
    private static partial Regex LengthAssignment();

    [GeneratedRegex(@"\b(?:const|let|var)\s*\{([^{}]*\b(?:durationSeconds|totalFrames|durationInFrames)\b[^{}]*)\}\s*=")]
    private static partial Regex LengthDestructuring();

    [GeneratedRegex(@"[\w$\)\]]\s*\??\.\s*caption\b")]
    private static partial Regex CaptionAccess();

    [GeneratedRegex(@"\[\s*['""`]caption['""`]\s*\]")]
    private static partial Regex CaptionKey();

    [GeneratedRegex(@"\{[^{}]*\bcaption\b[^{}]*\}\s*=|\(\s*\{[^{}]*\bcaption\b")]
    private static partial Regex CaptionDestructuring();

    [GeneratedRegex(@"font-family\s*:\s*([^;}\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CssFamily();

    [GeneratedRegex(@"\bfontFamily\s*[:=]\s*\{?\s*(['""`])((?:(?!\1).)*)\1")]
    private static partial Regex JsFamily();

    [GeneratedRegex(@"(?<![\w-])font\s*:\s*([^;}\n'""`]+)")]
    private static partial Regex CssShorthand();

    [GeneratedRegex(@"\bfont\s*[:=]\s*(['""`])((?:(?!\1).)*)\1")]
    private static partial Regex JsShorthand();

    [GeneratedRegex(@"\d+(?:\.\d+)?(?:px|pt|em|rem|%|vw|vh|vmin|vmax)(?:\s*/\s*[\d.]+[a-z%]*)?\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ShorthandFamilies();

    [GeneratedRegex(@"(?<![\w&#$])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{4}|[0-9a-fA-F]{3})(?![\w-])|\brgba?\(\s*[0-9.%\s,/]+\)", RegexOptions.IgnoreCase)]
    private static partial Regex Colour();
}
