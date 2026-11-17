using System.Text;
using System.Text.RegularExpressions;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Extractors;

/// <summary>
/// A Markdown file (.md; also the readable text the app keeps of a web page or article): every "#" heading starts a piece under the heading
/// path ("History › Trade"); links keep their text, images and HTML tags are dropped, emphasis marks are taken away. The first top heading is
/// the title.
/// </summary>
public sealed partial class MarkdownExtractor : DocumentExtractorBase
{
    /// <inheritdoc />
    public override DocumentKind Kind => DocumentKind.Markdown;

    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } = [".md"];

    /// <summary>The pieces of a Markdown text.</summary>
    public static ExtractedDocument Parse(string markdown)
    {
        var sections = new List<ExtractedSection>();
        var path = new List<(int Level, string Text)>();
        var body = new StringBuilder();
        var title = string.Empty;
        var fenced = false;

        void Flush()
        {
            if (body.Length > 0)
            {
                sections.Add(new ExtractedSection(0, string.Join(" › ", path.Select(p => p.Text)), body.ToString()));
                body.Clear();
            }
        }

        foreach (var raw in (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            var heading = fenced ? Match.Empty : Heading().Match(raw);
            if (heading.Success)
            {
                Flush();
                var level = heading.Groups[1].Value.Length;
                var text = Inline(heading.Groups[2].Value).Trim().TrimEnd('#').Trim();
                if (title.Length == 0 && level == 1)
                {
                    title = text;
                }

                path.RemoveAll(p => p.Level >= level);
                if (text.Length > 0)
                {
                    path.Add((level, text));
                }

                continue;
            }

            body.Append(fenced ? raw : Inline(raw)).Append('\n');
        }

        Flush();
        return new ExtractedDocument(title, sections);
    }

    /// <inheritdoc />
    protected override ExtractedDocument Read(string file, CancellationToken cancellationToken) => Parse(TextFiles.Read(file));

    /// <summary>One line without its Markdown marks.</summary>
    private static string Inline(string line)
    {
        var text = Image().Replace(line, string.Empty);
        text = Link().Replace(text, "$1");
        text = Tag().Replace(text, string.Empty);
        text = Emphasis().Replace(text, string.Empty);
        text = Quote().Replace(text, string.Empty);
        return text;
    }

    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"</?[a-zA-Z][^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"(\*\*|__|(?<!\w)[*_](?=\S)|(?<=\S)[*_](?!\w)|`)")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"^\s{0,3}>\s?")]
    private static partial Regex Quote();
}
