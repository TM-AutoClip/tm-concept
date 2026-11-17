using System.IO.Compression;
using System.Text;
using System.Xml;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Extractors;

/// <summary>
/// A Word document (.docx): its XML parts are read directly from the package (no Office, no macro, no external reference is followed;
/// DTDs are refused). Every paragraph in order (table cells too); a paragraph whose style is a heading ("heading 1"…, "Title", or any style
/// with an outline level) starts a piece under the heading path. Tabs become spaces, line breaks stay. The title comes from the document's
/// properties when it has one.
/// </summary>
public sealed class DocxExtractor : DocumentExtractorBase
{
    private const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Elements = "http://purl.org/dc/elements/1.1/";
    private const long PartBytesMax = 200L * 1024 * 1024;

    /// <inheritdoc />
    public override DocumentKind Kind => DocumentKind.Docx;

    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } = [".docx"];

    /// <inheritdoc />
    protected override ExtractedDocument Read(string file, CancellationToken cancellationToken)
    {
        try
        {
            using var zip = ZipFile.OpenRead(file);
            var body = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("The file is not a Word document (no word/document.xml).");
            var levels = zip.GetEntry("word/styles.xml") is { } styles ? HeadingStyles(styles) : [];
            var title = zip.GetEntry("docProps/core.xml") is { } core ? Title(core) : string.Empty;
            return new ExtractedDocument(title, Paragraphs(body, levels, cancellationToken));
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The Word document could not be read: " + ex.Message, ex);
        }
    }

    private static XmlReader Open(ZipArchiveEntry entry)
    {
        if (entry.Length > PartBytesMax)
        {
            throw new InvalidDataException("A part of the Word document is too large.");
        }

        return XmlReader.Create(entry.Open(), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            CloseInput = true,
        });
    }

    /// <summary>The paragraph styles that are headings, with their level (1 = top).</summary>
    private static Dictionary<string, int> HeadingStyles(ZipArchiveEntry entry)
    {
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        using var reader = Open(entry);
        string? id = null;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != Word)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "style":
                    id = reader.GetAttribute("type", Word) == "paragraph" ? reader.GetAttribute("styleId", Word) : null;
                    break;
                case "name" when id is not null:
                    var name = (reader.GetAttribute("val", Word) ?? string.Empty).ToUpperInvariant();
                    if (name == "TITLE")
                    {
                        levels[id] = 1;
                    }
                    else if (name.StartsWith("HEADING ", StringComparison.Ordinal) && int.TryParse(name[8..], out var level))
                    {
                        levels[id] = Math.Clamp(level, 1, 9);
                    }

                    break;
                case "outlineLvl" when id is not null:
                    if (int.TryParse(reader.GetAttribute("val", Word), out var outline) && outline is >= 0 and < 9)
                    {
                        levels.TryAdd(id, outline + 1);
                    }

                    break;
            }
        }

        return levels;
    }

    private static string Title(ZipArchiveEntry entry)
    {
        using var reader = Open(entry);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "title" && reader.NamespaceURI == Elements)
            {
                return reader.ReadElementContentAsString().Trim();
            }
        }

        return string.Empty;
    }

    private static List<ExtractedSection> Paragraphs(ZipArchiveEntry entry, Dictionary<string, int> styles, CancellationToken cancellationToken)
    {
        var sections = new List<ExtractedSection>();
        var path = new List<(int Level, string Text)>();
        var body = new StringBuilder();
        var paragraph = new StringBuilder();
        var level = 0;
        using var reader = Open(entry);

        void Flush()
        {
            if (body.Length > 0)
            {
                sections.Add(new ExtractedSection(0, string.Join(" › ", path.Select(p => p.Text)), body.ToString()));
                body.Clear();
            }
        }

        // ReadElementContentAsString leaves the reader on the node after the element, so that node is looked at before reading on.
        var more = reader.Read();
        while (more)
        {
            if (reader.NamespaceURI == Word && reader.NodeType == XmlNodeType.Element && reader.LocalName == "t" && !reader.IsEmptyElement)
            {
                paragraph.Append(reader.ReadElementContentAsString());
                more = !reader.EOF;
                continue;
            }

            if (reader.NamespaceURI == Word && reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "p":
                        cancellationToken.ThrowIfCancellationRequested();
                        paragraph.Clear();
                        level = 0;
                        if (reader.IsEmptyElement)
                        {
                            body.Append('\n');
                        }

                        break;
                    case "pStyle":
                        level = styles.GetValueOrDefault(reader.GetAttribute("val", Word) ?? string.Empty);
                        break;
                    case "outlineLvl":
                        if (int.TryParse(reader.GetAttribute("val", Word), out var outline) && outline is >= 0 and < 9)
                        {
                            level = outline + 1;
                        }

                        break;
                    case "tab":
                        paragraph.Append(' ');
                        break;
                    case "br" or "cr":
                        paragraph.Append('\n');
                        break;
                }
            }
            else if (reader.NamespaceURI == Word && reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p")
            {
                var text = paragraph.ToString().Trim();
                if (level > 0 && text.Length > 0)
                {
                    Flush();
                    path.RemoveAll(p => p.Level >= level);
                    path.Add((level, text.Replace('\n', ' ')));
                }
                else
                {
                    body.Append(text).Append("\n\n");
                }
            }

            more = reader.Read();
        }

        Flush();
        return sections;
    }
}
