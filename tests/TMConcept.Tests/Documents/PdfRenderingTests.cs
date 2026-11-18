using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Documents;
using TMConcept.Documents.Extractors;
using TMConcept.Documents.Pdf;
using TMConcept.Tests.Storage;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TMConcept.Tests.Documents;

/// <summary>
/// 0.1.27: a page without a usable text layer (no text, a few stray characters, a broken font map) is drawn whole by the PDF renderer and
/// handed to the engine as one JPEG; a page with text is never drawn; pages kept before are not drawn again; without the renderer (or when it
/// cannot draw) the page's images go as before; the drawn document is closed also on a cancel. A fake renderer and encoder: no PDFium here.
/// </summary>
public sealed class PdfRenderingTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData("Şehrin surları ve limanı, sayfa 12.", true)]
    [InlineData("The merchants paid in silver.", true)]
    [InlineData("abc", true)]
    [InlineData("", false)]
    [InlineData("   \n ", false)]
    [InlineData("12", false)]
    [InlineData("- 7 -", false)]
    [InlineData("���� ab��", false)]
    [InlineData(" abc ", false)]
    [InlineData("\u0001\u0002\u0003\u0004abc\u0005", false)]
    [InlineData("Ankara'nın kalesi � bir köşede.", true)]
    public void AUsableTextLayerHasLettersAndFewUnreadableCharacters(string text, bool usable) =>
        PageTextQuality.Usable(text).ShouldBe(usable);

    [Fact]
    public async Task PagesWithoutUsableTextAreDrawnWholeAndTextPagesNever()
    {
        using var paths = new TempPaths();
        var file = Write(paths, "mixed.pdf", Text, Image, Blank, Stray, Text);
        var renderer = new FakeRenderer();

        // Different words on every page (a line that repeats on most pages, digits aside, would be left out as a running header).
        var reader = new LargePdfTests.FakeRecognizer(page => "Okunan " + LargePdfTests.Line(page.Page));

        var read = await Extractor(renderer).ExtractAsync(file, new DocumentReadOptions(Recognizer: reader), None);

        reader.Pages.ShouldBe([2, 3, 4], "the scan, the drawn-only page and the page with a stray number");
        reader.Images.ShouldAllBe(i => i.MimeType == "image/jpeg");
        reader.Images.Count.ShouldBe(3, "one picture of the whole page each");
        renderer.Drawn.ShouldBe([2, 3, 4]);
        renderer.Opened.ShouldBe(1, "the file is opened once per reading");
        renderer.Closed.ShouldBe(1);
        (read.PagesRecognized, read.PagesUnread, read.PagesUnsupported, read.PagesWithoutText).ShouldBe((3, 0, 0, 0));
        read.Sections.Single(s => s.Page == 3).Text.ShouldBe("Okunan " + LargePdfTests.Line(3));
    }

    [Fact]
    public async Task WithoutTheRendererThePagesImagesGoAsBefore()
    {
        using var paths = new TempPaths();
        var file = Write(paths, "mixed.pdf", Text, Image, Blank, Stray);
        var reader = new LargePdfTests.FakeRecognizer(page => "Okunan sayfa " + page.Page);

        var read = await Extractor(new FakeRenderer { Ready = false }).ExtractAsync(file, new DocumentReadOptions(Recognizer: reader), None);

        reader.Pages.ShouldBe([2]);
        reader.Images.ShouldAllBe(i => i.MimeType == "image/png");
        (read.PagesRecognized, read.PagesUnread).ShouldBe((1, 2), "a page with nothing PdfPig can hand on stays unread");
    }

    [Fact]
    public async Task APageTheRendererCannotDrawFallsBackToItsImagesAndTheReadingGoesOn()
    {
        using var paths = new TempPaths();
        var file = Write(paths, "mixed.pdf", Image, Blank, Image);
        var renderer = new FakeRenderer { FailOn = [1, 2] };
        var reader = new LargePdfTests.FakeRecognizer(page => "Okunan sayfa " + page.Page);

        var read = await Extractor(renderer).ExtractAsync(file, new DocumentReadOptions(Recognizer: reader), None);

        reader.Pages.ShouldBe([1, 3], "page 1 went with its PNG, page 3 was drawn");
        reader.Images.Select(i => i.MimeType).ShouldBe(["image/png", "image/jpeg"]);
        (read.PagesRecognized, read.PagesUnread).ShouldBe((2, 1));
        renderer.Closed.ShouldBe(1);
    }

    [Fact]
    public async Task AFileTheRendererCannotOpenIsReadTheOldWay()
    {
        using var paths = new TempPaths();
        var file = Write(paths, "mixed.pdf", Image, Image);
        var renderer = new FakeRenderer { FailOpen = true };
        var reader = new LargePdfTests.FakeRecognizer(page => "Okunan sayfa " + page.Page);

        var read = await Extractor(renderer).ExtractAsync(file, new DocumentReadOptions(Recognizer: reader), None);

        reader.Pages.ShouldBe([1, 2]);
        reader.Images.ShouldAllBe(i => i.MimeType == "image/png");
        renderer.OpenTries.ShouldBe(1, "a file that cannot be opened is not tried again for every page");
        read.PagesRecognized.ShouldBe(2);
    }

    [Fact]
    public async Task APagePdfPigCannotParseIsDrawnAndTheOtherPagesAreStillRead()
    {
        using var paths = new TempPaths();
        var file = WriteWithBrokenPage(paths, "broken.pdf");
        var renderer = new FakeRenderer();
        var reader = new LargePdfTests.FakeRecognizer(page => "Okunan " + LargePdfTests.Line(page.Page));

        var read = await Extractor(renderer).ExtractAsync(file, new DocumentReadOptions(Recognizer: reader), None);

        renderer.Drawn.ShouldBe([2], "only the page PdfPig could not read");
        reader.Pages.ShouldBe([2]);
        read.Sections.Select(s => s.Page).ShouldBe([1, 2, 3]);
        read.Sections.Single(s => s.Page == 2).Text.ShouldBe("Okunan " + LargePdfTests.Line(2));
        (read.PagesRecognized, read.PagesUnread).ShouldBe((1, 0));

        // The renderer cannot draw it either: the page stays unread, the others are kept.
        var failing = new FakeRenderer { FailOn = [2] };
        var again = await Extractor(failing).ExtractAsync(file, new DocumentReadOptions(Recognizer: reader), None);
        again.Sections.Select(s => s.Page).ShouldBe([1, 3]);
        again.PagesUnread.ShouldBe(1);

        // Without the renderer (or without an engine) the page cannot be read at all: as before 0.1.27, the document is refused.
        await Should.ThrowAsync<InvalidDataException>(() => Extractor(new FakeRenderer { Ready = false }).ExtractAsync(file, new DocumentReadOptions(Recognizer: reader), None));
    }

    [Fact]
    public async Task AFilePdfPigCannotOpenIsDrawnPageByPageWhenTheRendererCan()
    {
        using var paths = new TempPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        var file = Path.Combine(paths.DataDirectory, "locked.pdf");
        File.WriteAllText(file, "%PDF-1.7\nnot something PdfPig can open\n");
        var renderer = new FakeRenderer { PageCount = 3 };
        var reader = new LargePdfTests.FakeRecognizer(page => "Okunan " + LargePdfTests.Line(page.Page));

        var read = await Extractor(renderer).ExtractAsync(file, new DocumentReadOptions(Recognizer: reader), None);

        renderer.Drawn.ShouldBe([1, 2, 3]);
        (read.PagesRecognized, read.Sections.Count).ShouldBe((3, 3));
        renderer.Closed.ShouldBe(1);

        await Should.ThrowAsync<InvalidDataException>(() => Extractor(new FakeRenderer { FailOpen = true }).ExtractAsync(file, new DocumentReadOptions(Recognizer: reader), None));
        await Should.ThrowAsync<InvalidDataException>(() => Extractor(renderer).ExtractAsync(file, None), "without an engine nothing is drawn");
    }

    [Fact]
    public async Task KeptPagesAreNotDrawnAgain()
    {
        using var paths = new TempPaths();
        var file = Write(paths, "scan.pdf", Image, Image, Image);
        var memory = new FakeMemory();
        await memory.KeepAsync(1, "Daha önce okundu", PageTextSources.OcrPrefix + "test", None);
        await memory.KeepAsync(3, string.Empty, PageTextSources.OcrPrefix + "test", None);
        var renderer = new FakeRenderer();
        var reader = new LargePdfTests.FakeRecognizer(page => "Okunan sayfa " + page.Page);

        var read = await Extractor(renderer).ExtractAsync(file, new DocumentReadOptions(Recognizer: reader) { Memory = memory }, None);

        renderer.Drawn.ShouldBe([2]);
        reader.Pages.ShouldBe([2]);
        read.PagesReused.ShouldBe(2);
        memory.Kept[2].ShouldBe("Okunan sayfa 2", "the new reading is kept at once");
    }

    [Fact]
    public async Task ACancelClosesTheDrawnDocument()
    {
        using var paths = new TempPaths();
        var file = Write(paths, "scan.pdf", Image, Image, Image);
        var renderer = new FakeRenderer();
        using var cancel = new CancellationTokenSource();
        var reader = new LargePdfTests.FakeRecognizer(page =>
        {
            cancel.Cancel();
            return "Okunan sayfa " + page.Page;
        });

        await Should.ThrowAsync<OperationCanceledException>(() => Extractor(renderer).ExtractAsync(file, new DocumentReadOptions(Recognizer: reader), cancel.Token));

        renderer.Opened.ShouldBe(1);
        renderer.Closed.ShouldBe(1);
        renderer.Drawn.ShouldBe([1]);
    }

    [Fact]
    public async Task WithoutAnEngineNothingIsDrawnAndTheQuickLookCountsStrayTextAsAScan()
    {
        using var paths = new TempPaths();
        var file = Write(paths, "mixed.pdf", Text, Stray, Blank, Text);
        var renderer = new FakeRenderer();

        var plain = await Extractor(renderer).ExtractAsync(file, None);
        var probe = await Extractor(renderer).ProbeAsync(file, None);

        renderer.Opened.ShouldBe(0, "no engine: nothing to draw for");
        plain.PagesWithoutText.ShouldBe(2);
        (probe.Sampled, probe.SampledWithoutText).ShouldBe((4, 2));
    }

    [Fact]
    public void TheDrawnPageBecomesAJpegTheOcrCanDecodeAndABusyPageIsMadeToFit()
    {
        var pixels = new byte[300 * 200 * 4];
        Array.Fill(pixels, (byte)255);
        for (var x = 50; x < 250; x++)
        {
            pixels[((100 * 300) + x) * 4] = 0;
            pixels[(((100 * 300) + x) * 4) + 1] = 0;
            pixels[(((100 * 300) + x) * 4) + 2] = 0;
        }

        var jpeg = new TMConcept.Desktop.Documents.SkiaPageImageEncoder().Encode(new RenderedPage(300, 200, pixels));
        jpeg.MimeType.ShouldBe("image/jpeg");
        var decoded = new TMConcept.Desktop.Documents.SkiaPageImageDecoder().Decode(jpeg);
        (decoded.Width, decoded.Height).ShouldBe((300, 200));
        decoded.Rgb[((100 * 300) + 150) * 3].ShouldBeLessThan((byte)64, "the black line is still there");

        // Noise does not compress: the encoder lowers the quality, then the size, until the picture fits.
        var noise = new byte[2400 * 2400 * 4];
        new Random(7).NextBytes(noise);
        var fitted = new TMConcept.Desktop.Documents.SkiaPageImageEncoder().Encode(new RenderedPage(2400, 2400, noise));
        fitted.Data.Length.ShouldBeLessThanOrEqualTo(DocumentLimits.PageImageBytesMax);
        Should.Throw<InvalidDataException>(() => new TMConcept.Desktop.Documents.SkiaPageImageEncoder().Encode(new RenderedPage(10, 10, new byte[8])));
    }

    private static PdfExtractor Extractor(FakeRenderer renderer) => new(renderer, new FakeEncoder(), NullLogger<PdfExtractor>.Instance);

    private const int Text = 0;
    private const int Image = 1;
    private const int Blank = 2;
    private const int Stray = 3;

    /// <summary>A PDF whose pages are of the given kinds: a line of text, a scan (an image), nothing drawn with text or image, a lone page number.</summary>
    private static string Write(TempPaths paths, string name, params int[] kinds)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var png = LargePdfTests.Png(96, 96);
        for (var i = 0; i < kinds.Length; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            switch (kinds[i])
            {
                case Text:
                    page.AddText(LargePdfTests.Line(i + 1), 8, new PdfPoint(30, 780), font);
                    break;
                case Image:
                    page.AddPng(png, new PdfRectangle(40, 400, 400, 760));
                    break;
                case Blank:
                    page.DrawLine(new PdfPoint(40, 400), new PdfPoint(400, 760));
                    break;
                default:
                    page.AddText((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 8, new PdfPoint(300, 30), font);
                    break;
            }
        }

        Directory.CreateDirectory(paths.DataDirectory);
        var file = Path.Combine(paths.DataDirectory, name);
        File.WriteAllBytes(file, builder.Build());
        return file;
    }

    /// <summary>
    /// A PDF of three pages written by hand: text, a page whose text uses a font its resources do not have (PdfPig cannot read it), text.
    /// </summary>
    private static string WriteWithBrokenPage(TempPaths paths, string name)
    {
        string Content(int page, string font) => "BT /" + font + " 12 Tf 72 712 Td (" + LargePdfTests.Line(page) + ") Tj ET";
        var contents = new[] { Content(1, "F1"), Content(2, "F9"), Content(3, "F1") };
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>",
        };
        for (var i = 0; i < 3; i++)
        {
            objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 9 0 R >> >> /Contents " + (6 + i) + " 0 R >>");
        }

        objects.AddRange(contents.Select(c => "<< /Length " + c.Length + " >>\nstream\n" + c + "\nendstream"));
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Directory.CreateDirectory(paths.DataDirectory);
        var file = Path.Combine(paths.DataDirectory, name);
        File.WriteAllText(file, pdf.ToString(), System.Text.Encoding.ASCII);
        return file;
    }

    /// <summary>Draws a page as a white picture of 10 × 14 pixels; may refuse given pages, or the file.</summary>
    private sealed class FakeRenderer : IPdfPageRenderer
    {
        public bool Ready { get; init; } = true;

        public int PageCount { get; init; } = 99;

        public bool FailOpen { get; init; }

        public int[] FailOn { get; init; } = [];

        public int Opened { get; private set; }

        public int OpenTries { get; private set; }

        public int Closed { get; private set; }

        public List<int> Drawn { get; } = [];

        public IPdfRenderDocument Open(string file)
        {
            OpenTries++;
            if (FailOpen)
            {
                throw new InvalidDataException("PDFium could not open the PDF (not a PDF, or damaged).");
            }

            Opened++;
            return new Document(this);
        }

        private sealed class Document(FakeRenderer owner) : IPdfRenderDocument
        {
            public int Pages => owner.PageCount;

            public RenderedPage Render(int page, int dpi, long pixelsMax)
            {
                dpi.ShouldBe(PdfExtractor.RenderDpi);
                pixelsMax.ShouldBe(PdfExtractor.RenderPixelsMax);
                if (owner.FailOn.Contains(page))
                {
                    throw new InvalidDataException("PDFium could not load page " + page + ".");
                }

                owner.Drawn.Add(page);
                var pixels = new byte[10 * 14 * 4];
                Array.Fill(pixels, (byte)255);
                return new RenderedPage(10, 14, pixels);
            }

            public void Dispose() => owner.Closed++;
        }
    }

    /// <summary>"Encodes" a drawn page as a few bytes marked as a JPEG.</summary>
    private sealed class FakeEncoder : IPageImageEncoder
    {
        public ImagePart Encode(RenderedPage page) => new([0xFF, 0xD8, 0xFF, (byte)page.Width, (byte)page.Height], "image/jpeg");
    }

    private sealed class FakeMemory : IPageTextMemory
    {
        public Dictionary<int, string> Kept { get; } = [];

        public DocumentPageText? Find(int page) =>
            Kept.TryGetValue(page, out var text) ? new DocumentPageText { Page = page, Text = text, Source = PageTextSources.OcrPrefix + "test" } : null;

        public Task KeepAsync(int page, string text, string source, CancellationToken cancellationToken)
        {
            Kept[page] = text;
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
