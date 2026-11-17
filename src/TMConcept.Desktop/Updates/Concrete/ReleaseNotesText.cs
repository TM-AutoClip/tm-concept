using System.Text.RegularExpressions;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// The release notes as the update window shows them: only the part in the app's language (our notes have a <c>## English</c> and a
/// <c>## Türkçe</c> section, <c>docs/release-notes/</c>; another language or a missing section falls back to English, notes without sections
/// are shown whole), never the SHA-256 block, and the Markdown turned into plain readable text (headings as their text, bullets as "•",
/// bold, code and links as their text, wrapped lines joined).
/// </summary>
public static partial class ReleaseNotesText
{
    private const int Blank = 0;
    private const int Heading = 1;
    private const int Text = 2;

    private static readonly string[] English = ["english", "ingilizce"];

    private static readonly string[] Turkish = ["türkçe", "turkce", "turkish"];

    /// <summary>The readable notes of <paramref name="body"/> in <paramref name="language"/> ("tr" or "en").</summary>
    public static string For(string? body, string language) => Readable(Section(body, language));

    /// <summary>
    /// The part of <paramref name="body"/> in <paramref name="language"/>: the text under its level-two heading up to the next level-two
    /// heading; English when the language has none; the whole text when there are no language sections. The SHA-256 block is always removed.
    /// </summary>
    public static string Section(string? body, string language)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var wanted = language == "tr" ? Turkish : English;
        var picked = Find(lines, wanted) ?? Find(lines, English);
        var part = picked is { } range ? lines[range] : lines;
        return string.Join('\n', WithoutHashes(part)).Trim();
    }

    /// <summary>Markdown as plain readable text (see the class).</summary>
    public static string Readable(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var output = new List<string>();
        var previous = Blank;
        var fence = false;
        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                fence = !fence;
                continue;
            }

            if (fence)
            {
                output.Add(line);
                previous = Text;
                continue;
            }

            if (line.Trim().Length == 0)
            {
                previous = AddBlank(output, previous);
                continue;
            }

            if (Rule().IsMatch(line))
            {
                previous = AddBlank(output, previous);
                continue;
            }

            if (HeadingLine().Match(line) is { Success: true } heading)
            {
                AddBlank(output, previous);
                output.Add(Inline(heading.Groups[1].Value.Trim().TrimEnd('#').Trim()));
                previous = Heading;
                continue;
            }

            if (Bullet().Match(line) is { Success: true } bullet)
            {
                var depth = bullet.Groups[1].Value.Replace("\t", "    ", StringComparison.Ordinal).Length / 2;
                output.Add(new string(' ', Math.Min(depth, 4) * 2) + "• " + Inline(bullet.Groups[2].Value.Trim()));
                previous = Text;
                continue;
            }

            if (Numbered().Match(line) is { Success: true } numbered)
            {
                output.Add(numbered.Groups[1].Value.Trim() + " " + Inline(numbered.Groups[2].Value.Trim()));
                previous = Text;
                continue;
            }

            // A wrapped line (indented under a bullet, or the next line of a paragraph) joins the line before it.
            var text = Inline(line.Trim().TrimStart('>').Trim());
            if (previous == Text && output.Count > 0)
            {
                output[^1] += " " + text;
            }
            else
            {
                output.Add(text);
                previous = Text;
            }
        }

        return string.Join('\n', output).Trim();
    }

    /// <summary>Bold, italics with stars, code and links as their text; backslash escapes undone.</summary>
    internal static string Inline(string text)
    {
        var result = Image().Replace(text, "$1");
        result = Link().Replace(result, "$1");
        result = Bold().Replace(result, "$2");
        result = Code().Replace(result, "$1");
        result = StarItalic().Replace(result, "$1");
        return Escape().Replace(result, "$1");
    }

    private static int AddBlank(List<string> output, int previous)
    {
        if (previous != Blank && output.Count > 0)
        {
            output.Add(string.Empty);
        }

        return Blank;
    }

    /// <summary>The lines under the first level-two heading named one of <paramref name="names"/>, up to the next level-two heading.</summary>
    private static Range? Find(string[] lines, string[] names)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (Level(lines[i]) != 2 || !names.Contains(Title(lines[i]).ToLower(System.Globalization.CultureInfo.InvariantCulture).Replace("i̇", "i", StringComparison.Ordinal), StringComparer.Ordinal))
            {
                continue;
            }

            var end = i + 1;
            while (end < lines.Length && Level(lines[end]) is 0 or > 2)
            {
                end++;
            }

            return (i + 1)..end;
        }

        return null;
    }

    /// <summary>The lines without any heading that names SHA-256 and what is under it (up to the next heading of the same or a higher level).</summary>
    private static IEnumerable<string> WithoutHashes(IEnumerable<string> lines)
    {
        var skipping = 0;
        foreach (var line in lines)
        {
            var level = Level(line);
            if (skipping > 0 && level > 0 && level <= skipping)
            {
                skipping = 0;
            }

            if (skipping == 0 && level > 0 && Hash().IsMatch(Title(line)))
            {
                skipping = level;
            }

            if (skipping == 0)
            {
                yield return line;
            }
        }
    }

    /// <summary>The level of a Markdown heading line (1–6), 0 when it is not one.</summary>
    private static int Level(string line)
    {
        var match = HeadingLine().Match(line);
        return match.Success ? line.TrimStart().TakeWhile(c => c == '#').Count() : 0;
    }

    private static string Title(string line)
    {
        var match = HeadingLine().Match(line);
        if (!match.Success)
        {
            return string.Empty;
        }

        return Inline(match.Groups[1].Value.Trim().TrimEnd('#').Trim()).Trim();
    }

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(.*)$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^(\s*)[-*+]\s+(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s*(\d+[.)])\s+(.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(\*\*|__)(.+?)\1")]
    private static partial Regex Bold();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"(?<![\w*])\*(?!\s)([^*\n]+?)(?<!\s)\*(?![\w*])")]
    private static partial Regex StarItalic();

    [GeneratedRegex(@"\\([\\`*_{}\[\]()#+\-.!>])")]
    private static partial Regex Escape();

    [GeneratedRegex(@"SHA-?256", RegexOptions.IgnoreCase)]
    private static partial Regex Hash();
}
