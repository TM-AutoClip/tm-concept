using System.IO.Compression;
using System.Text;
using Shouldly;
using TMConcept.Core.Documents;
using TMConcept.Documents.Extractors;
using TMConcept.Tests.Storage;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TMConcept.Tests.Documents;

/// <summary>Each document reader on a small file made by the test (PDF, Word, text, Markdown), Turkish letters included.</summary>
public sealed class ExtractorTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task APdfIsReadPageByPageAndAPageWithoutTextIsCounted()
    {
        using var paths = new TempPaths();
        var file = Path.Combine(paths.DataDirectory, "trade.pdf");
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Silk came to Constantinople by caravan.", 12, new PdfPoint(40, 700), font);
        builder.AddPage(PageSize.A4);
        builder.AddPage(PageSize.A4).AddText("The emperor taxed the merchants.", 12, new PdfPoint(40, 700), font);
        Directory.CreateDirectory(paths.DataDirectory);
        await File.WriteAllBytesAsync(file, builder.Build(), None);

        var document = await new PdfExtractor().ExtractAsync(file, None);

        document.Pages.ShouldBe(3);
        document.PagesWithoutText.ShouldBe(1);
        document.Sections.Select(s => s.Page).ShouldBe([1, 3]);
        document.Sections[0].Text.ShouldContain("Silk came to Constantinople by caravan.");
        document.Sections[1].Text.ShouldContain("The emperor taxed the merchants.");
    }

    [Fact]
    public async Task ADamagedPdfIsRefusedAsUnreadable()
    {
        using var paths = new TempPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        var file = Path.Combine(paths.DataDirectory, "broken.pdf");
        await File.WriteAllTextAsync(file, "%PDF-1.7 this is not a real pdf", None);
        await Should.ThrowAsync<InvalidDataException>(() => new PdfExtractor().ExtractAsync(file, None));
    }

    [Fact]
    public async Task AWordDocumentKeepsItsHeadingPathTitleAndTurkishLetters()
    {
        using var paths = new TempPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        var file = Path.Combine(paths.DataDirectory, "tarih.docx");
        WriteDocx(file);

        var document = await new DocxExtractor().ExtractAsync(file, None);

        document.Title.ShouldBe("Bizans Tarihi");
        document.Sections.Select(s => s.Heading).ShouldBe(["", "Ticaret", "Ticaret › İpek Yolu", "Surlar"]);
        document.Sections[0].Text.ShouldBe("Giriş paragrafı: şehir, ırmak, çağ, öğün.");
        document.Sections[2].Text.ShouldContain("Kervanlar ipeği taşıdı.");
        document.Sections[2].Text.ShouldContain("Tablo hücresi");
        document.Sections[3].Text.ShouldBe("Üç sıra sur vardı.\nİkinci satır sonu");
    }

    [Fact]
    public async Task NotAWordDocumentIsRefused()
    {
        using var paths = new TempPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        var file = Path.Combine(paths.DataDirectory, "fake.docx");
        await File.WriteAllTextAsync(file, "plain text", None);
        await Should.ThrowAsync<InvalidDataException>(() => new DocxExtractor().ExtractAsync(file, None));
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8bom")]
    [InlineData("utf16")]
    [InlineData("latin1")]
    public async Task ATextFileIsReadInItsEncoding(string encoding)
    {
        using var paths = new TempPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        var file = Path.Combine(paths.DataDirectory, "notes.txt");
        var text = encoding == "latin1" ? "Café crème\r\nzweite Zeile" : "Ilık çay, ğ ş ı İ\r\nikinci satır";
        byte[] bytes = encoding switch
        {
            "utf8bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
            "utf16" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
            "latin1" => Encoding.Latin1.GetBytes(text),
            _ => Encoding.UTF8.GetBytes(text),
        };
        await File.WriteAllBytesAsync(file, bytes, None);

        var document = await new PlainTextExtractor().ExtractAsync(file, None);

        document.Sections.ShouldHaveSingleItem().Text.ShouldBe(text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void MarkdownHeadingsBecomeTheHeadingPathAndMarksGo()
    {
        var document = MarkdownExtractor.Parse("""
            # The Silk Road
            Intro with a [link](https://example.org) and ![image](a.png) and **bold**.

            ## Trade
            Caravans *carried* silk.

            ```
            # not a heading
            ```

            ### Prices
            High.

            ## Walls
            > Three lines.
            """);

        document.Title.ShouldBe("The Silk Road");
        document.Sections.Select(s => s.Heading).ShouldBe(["The Silk Road", "The Silk Road › Trade", "The Silk Road › Trade › Prices", "The Silk Road › Walls"]);
        document.Sections[0].Text.Trim().ShouldBe("Intro with a link and  and bold.");
        document.Sections[1].Text.ShouldContain("Caravans carried silk.");
        document.Sections[1].Text.ShouldContain("# not a heading", Case.Sensitive, "a fenced block is kept as it is");
        document.Sections[3].Text.Trim().ShouldBe("Three lines.");
    }

    [Fact]
    public async Task ATooLargeFileIsRefusedBeforeItIsRead()
    {
        using var paths = new TempPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        var file = Path.Combine(paths.DataDirectory, "huge.txt");
        await using (var stream = File.Create(file))
        {
            stream.SetLength(DocumentLimits.FileBytesMax + 1);
        }

        await Should.ThrowAsync<InvalidDataException>(() => new PlainTextExtractor().ExtractAsync(file, None));
    }

    /// <summary>A small .docx: styles with a heading 1 and heading 2 (style ids in Turkish, as a Turkish Word writes them), a table, a break.</summary>
    private static void WriteDocx(string file)
    {
        const string w = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
        static string P(string text, string? style = null) =>
            "<w:p>" + (style is null ? string.Empty : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>") + $"<w:r><w:t>{text}</w:t></w:r></w:p>";
        var body = P("Giriş paragrafı: şehir, ırmak, çağ, öğün.") + P("Ticaret", "Balk1") + P("Yollar uzundu.") + P("İpek Yolu", "Balk2")
            + P("Kervanlar ipeği taşıdı.") + "<w:tbl><w:tr><w:tc>" + P("Tablo hücresi") + "</w:tc></w:tr></w:tbl>" + P("Surlar", "Balk1")
            + "<w:p><w:r><w:t>Üç sıra sur vardı.</w:t><w:br/><w:t>İkinci satır</w:t><w:tab/><w:t>sonu</w:t></w:r></w:p>";
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        void Part(string name, string xml)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }

        Part("[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
        Part("word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document {w}><w:body>{body}</w:body></w:document>");
        Part("word/styles.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:styles {w}>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Balk1\"><w:name w:val=\"heading 1\"/></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Balk2\"><w:name w:val=\"heading 2\"/></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style></w:styles>");
        Part("docProps/core.xml", "<?xml version=\"1.0\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" "
            + "xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>Bizans Tarihi</dc:title></cp:coreProperties>");
    }
}
