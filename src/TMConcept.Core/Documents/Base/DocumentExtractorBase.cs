using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Core.Documents;

/// <summary>
/// The common part of reading a document file: a missing or too large file is refused before it is read (<see cref="DocumentLimits.FileBytesMax"/>);
/// the kind's own reader runs off the calling thread; then the text is cleaned the same way for every kind: line ends unified, control
/// characters removed, a word broken over a line end with a hyphen joined again, runs of spaces made one, a header or footer line that
/// repeats on most pages left out, empty pieces dropped, and the whole cut at <see cref="DocumentLimits.CharactersMax"/>.
/// </summary>
public abstract partial class DocumentExtractorBase : IDocumentExtractor
{
    /// <inheritdoc />
    public abstract DocumentKind Kind { get; }

    /// <inheritdoc />
    public abstract IReadOnlyList<string> Extensions { get; }

    /// <inheritdoc />
    public async Task<ExtractedDocument> ExtractAsync(string file, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        var info = new FileInfo(file);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The document file is not there.", file);
        }

        if (info.Length > DocumentLimits.FileBytesMax)
        {
            throw new InvalidDataException($"The file is larger than {DocumentLimits.FileBytesMax >> 20} MB.");
        }

        var read = await Task.Run(() => Read(file, cancellationToken), cancellationToken).ConfigureAwait(false);
        return Clean(read);
    }

    /// <summary>Reads the kind's text (<see cref="InvalidDataException"/> when the file is not of this kind or is damaged).</summary>
    protected abstract ExtractedDocument Read(string file, CancellationToken cancellationToken);

    /// <summary>The cleaned document (see the class summary).</summary>
    public static ExtractedDocument Clean(ExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var repeated = RepeatedLines(document);
        var sections = new List<ExtractedSection>();
        var left = DocumentLimits.CharactersMax;
        foreach (var section in document.Sections)
        {
            var text = CleanText(section.Text, repeated);
            if (text.Length == 0 || left <= 0)
            {
                continue;
            }

            if (text.Length > left)
            {
                text = text[..left];
            }

            left -= text.Length;
            sections.Add(section with { Heading = CleanText(section.Heading, null).Replace('\n', ' '), Text = text });
        }

        return document with { Title = CleanText(document.Title, null).Replace('\n', ' '), Sections = sections };
    }

    /// <summary>Whether <paramref name="document"/> was cut at <see cref="DocumentLimits.CharactersMax"/>.</summary>
    public static bool IsCut(ExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Characters >= DocumentLimits.CharactersMax;
    }

    /// <summary>One piece of text cleaned; lines in <paramref name="drop"/> are left out.</summary>
    public static string CleanText(string? text, IReadOnlySet<string>? drop)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var unified = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace(' ', ' ');
        var builder = new StringBuilder(unified.Length);
        foreach (var c in unified)
        {
            if (c == '\n' || c == '\t' || !char.IsControl(c))
            {
                builder.Append(c == '\t' ? ' ' : c);
            }
        }

        var joined = Hyphenated().Replace(builder.ToString(), "$1$2");
        var lines = joined.Split('\n').Select(l => Spaces().Replace(l, " ").Trim()).Where(l => drop is null || !drop.Contains(l));
        return Blank().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    /// <summary>
    /// The first and last lines of pages that repeat on more than half of the pages (running headers, footers, page numbers made of digits
    /// are not compared exactly, so "Page 3" and "Page 4" count as one); only for documents of three pages or more.
    /// </summary>
    private static HashSet<string> RepeatedLines(ExtractedDocument document)
    {
        var pages = document.Sections.Where(s => s.Page > 0).GroupBy(s => s.Page).ToList();
        var lines = new HashSet<string>(StringComparer.Ordinal);
        if (pages.Count < 3)
        {
            return lines;
        }

        var counts = new Dictionary<string, (int Count, List<string> Lines)>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            var text = string.Join('\n', page.Select(s => s.Text)).Split('\n').Select(l => Spaces().Replace(l, " ").Trim()).Where(l => l.Length > 0).ToList();
            var edges = text.Take(2).Concat(text.TakeLast(2)).Distinct(StringComparer.Ordinal);
            foreach (var line in edges)
            {
                var key = Digits().Replace(line, "#");
                counts[key] = counts.TryGetValue(key, out var seen) ? (seen.Count + 1, [.. seen.Lines, line]) : (1, [line]);
            }
        }

        foreach (var (_, value) in counts.Where(c => c.Value.Count * 2 > pages.Count))
        {
            lines.UnionWith(value.Lines);
        }

        return lines;
    }

    [GeneratedRegex(@"(\p{L})-\n(\p{Ll})")]
    private static partial Regex Hyphenated();

    [GeneratedRegex(@"[ ]{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex Blank();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
