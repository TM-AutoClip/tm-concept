using System.Buffers.Binary;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Documents;
using TMConcept.Core.Providers;
using TMConcept.Documents.Extractors;
using TMConcept.Tests.Storage;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TMConcept.Tests.Documents;

/// <summary>
/// 0.1.24, large PDFs: a page range is read alone, a long PDF is read page by page (reopened on the way, every page reported, a cancel stops
/// between pages), a quick look samples the pages, a scanned page (an image, no text layer) is found and read by a text-extraction model
/// (a fake one here), and failing pages are counted until too many fail in a row. PDFs are made by the test; nothing goes to a network.
/// </summary>
public sealed class LargePdfTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData("", 700, true, 0, 0)]
    [InlineData("all", 700, true, 0, 0)]
    [InlineData("tümü", 700, true, 0, 0)]
    [InlineData("120-180", 700, true, 120, 180)]
    [InlineData(" 120 – 180 ", 700, true, 120, 180)]
    [InlineData("120", 700, true, 120, 120)]
    [InlineData("650-", 700, true, 650, 700)]
    [InlineData("-50", 700, true, 1, 50)]
    [InlineData("1-700", 700, true, 0, 0)]
    [InlineData("180-120", 700, false, 0, 0)]
    [InlineData("0-5", 700, false, 0, 0)]
    [InlineData("690-720", 700, false, 0, 0)]
    [InlineData("abc", 700, false, 0, 0)]
    [InlineData("650-", 0, false, 0, 0)]
    [InlineData("5-9000", 0, true, 5, 9000)]
    public void APageRangeIsReadFromWhatTheUserTyped(string text, int pages, bool valid, int from, int to)
    {
        DocumentPageRange.TryParse(text, pages, out var range).ShouldBe(valid);
        if (valid)
        {
            (range?.From ?? 0, range?.To ?? 0).ShouldBe((from, to));
        }
    }

    [Fact]
    public async Task OnlyThePagesOfTheRangeAreReadAndTheyKeepTheirOwnNumbers()
    {
        using var paths = new TempPaths();
        var file = WritePdf(paths, "five.pdf", 5, _ => true);

        var document = await new PdfExtractor().ExtractAsync(file, new DocumentReadOptions(new DocumentPageRange(2, 4)), None);

        document.Pages.ShouldBe(3, "the pages read");
        document.Sections.Select(s => s.Page).ShouldBe([2, 3, 4]);
        document.Sections[0].Text.ShouldContain(Line(2));
        var outside = await Should.ThrowAsync<InvalidDataException>(() => new PdfExtractor().ExtractAsync(file, new DocumentReadOptions(new DocumentPageRange(9, 12)), None));
        outside.Message.ShouldContain("5 pages");
    }

    [Fact]
    public async Task ALongPdfIsReadPageByPageWithEveryPageReportedAndACancelStopsBetweenPages()
    {
        using var paths = new TempPaths();
        const int pages = 300;
        var file = WritePdf(paths, "volume.pdf", pages, _ => true);
        var reports = new List<DocumentProgress>();

        var document = await new PdfExtractor().ExtractAsync(file, new DocumentReadOptions(Progress: new Sync<DocumentProgress>(reports.Add), Name: "volume.pdf"), None);

        pages.ShouldBeGreaterThan(DocumentLimits.PdfReopenPages * 4, "the reader is opened again several times on the way");
        document.Pages.ShouldBe(pages);
        document.Sections.Count.ShouldBe(pages);
        document.Sections[^1].Text.ShouldContain(Line(300));
        reports.ShouldAllBe(r => r.Stage == DocumentStage.Extracting && r.Name == "volume.pdf" && r.Total == pages);
        reports.Select(r => r.Done).ShouldBe(Enumerable.Range(0, pages + 1));

        using var cancel = new CancellationTokenSource();
        var seen = 0;
        var progress = new Sync<DocumentProgress>(p =>
        {
            if (++seen == 40)
            {
                cancel.Cancel();
            }
        });
        await Should.ThrowAsync<OperationCanceledException>(() => new PdfExtractor().ExtractAsync(file, new DocumentReadOptions(Progress: progress), cancel.Token));
        seen.ShouldBe(40, "nothing is read after the cancel");
    }

    [Fact]
    public async Task AQuickLookSamplesThePagesAndFindsTheScannedOnes()
    {
        using var paths = new TempPaths();
        var file = WritePdf(paths, "mixed.pdf", 40, page => page <= 10);

        var probe = await new PdfExtractor().ProbeAsync(file, None);

        probe.Pages.ShouldBe(40);
        probe.Sampled.ShouldBe(DocumentLimits.ProbePages);
        probe.SampledWithoutText.ShouldBeInRange(8, 10, "about three quarters of the sampled pages are images");
        probe.HasImagePages.ShouldBeTrue();
        probe.ImagePagesAbout(40).ShouldBeInRange(26, 34);
        PdfExtractor.Sample(40, 12).ShouldBe([1, 5, 8, 12, 15, 19, 22, 26, 29, 33, 36, 40]);
        PdfExtractor.Sample(5, 12).ShouldBe([1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task AScannedPageIsCountedOrReadByTheTextExtractionModel()
    {
        using var paths = new TempPaths();
        var file = WritePdf(paths, "scan.pdf", 4, page => page % 2 == 1);

        var plain = await new PdfExtractor().ExtractAsync(file, None);
        (plain.Pages, plain.PagesWithoutText, plain.PagesRecognized).ShouldBe((4, 2, 0));

        var reader = new FakeRecognizer(page => "Şehrin surları, sayfa " + page.Page + ".");
        var read = await new PdfExtractor().ExtractAsync(file, new DocumentReadOptions(Recognizer: reader), None);

        (read.Pages, read.PagesWithoutText, read.PagesRecognized, read.PagesUnread).ShouldBe((4, 0, 2, 0));
        read.Sections.Select(s => s.Page).ShouldBe([1, 2, 3, 4]);
        read.Sections[1].Text.ShouldBe("Şehrin surları, sayfa 2.", "Turkish letters come through as the model read them");
        reader.Pages.ShouldBe([2, 4]);
        reader.Images.ShouldAllBe(i => i.MimeType == "image/png" && i.Data.Length > 0, "the page's image is handed on (a PNG here)");
    }

    [Fact]
    public async Task FailingPagesAreCountedAndTooManyInARowOrAWrongKeyStopTheExtractionButKeepTheText()
    {
        using var paths = new TempPaths();
        var file = WritePdf(paths, "scan.pdf", 6, page => page is 1 or 3);

        var once = 0;
        var flaky = new FakeRecognizer(page => ++once == 1 ? throw new ProviderException("busy") { Code = "busy" } : "text " + page.Page);
        var read = await new PdfExtractor().ExtractAsync(file, new DocumentReadOptions(Recognizer: flaky), None);
        (read.PagesRecognized, read.PagesUnread, read.PagesWithoutText).ShouldBe((3, 1, 1));
        (flaky.Recognized, flaky.Failed).ShouldBe((3, 1));

        var broken = new FakeRecognizer(_ => throw new ProviderException("the service is busy") { Code = "busy" });
        var stopped = await new PdfExtractor().ExtractAsync(file, new DocumentReadOptions(Recognizer: broken), None);
        broken.Failed.ShouldBe(DocumentLimits.RecognitionFailuresInARowMax, "no page is asked for after the stop");
        stopped.RecognitionStopped.ShouldBe("busy", "the provider's error code: the page says it in the user's language");
        stopped.PagesUnread.ShouldBe(4);
        stopped.Sections.Select(s => s.Page).ShouldBe([1, 3], "the text pages are still read and kept");

        var key = new FakeRecognizer(_ => throw new ProviderException("bad key") { Code = "auth" });
        var wrong = await new PdfExtractor().ExtractAsync(file, new DocumentReadOptions(Recognizer: key), None);
        key.Failed.ShouldBe(1, "a wrong key fails every page: the engine is not asked again");
        wrong.RecognitionStopped.ShouldBe("auth");
    }

    [Fact]
    public async Task APdfLargerThanATextFileIsAccepted()
    {
        new PdfExtractor().MaxBytes.ShouldBe(DocumentLimits.PdfBytesMax);
        new PlainTextExtractor().MaxBytes.ShouldBe(DocumentLimits.FileBytesMax);
        DocumentLimits.PdfBytesMax.ShouldBeGreaterThan(DocumentLimits.FileBytesMax);
        using var paths = new TempPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        var big = Path.Combine(paths.DataDirectory, "big.pdf");
        await using (var stream = File.Create(big))
        {
            stream.SetLength(DocumentLimits.FileBytesMax + 1);
        }

        // Past the size check: it is not refused for its size, only for not being a PDF.
        var error = await Should.ThrowAsync<InvalidDataException>(() => new PdfExtractor().ProbeAsync(big, None));
        error.Message.ShouldStartWith("The PDF could not be read");
    }

    /// <summary>
    /// A PDF of <paramref name="pages"/> pages: a page for which <paramref name="text"/> is true has a line of text, any other only an image
    /// (a scan).
    /// </summary>
    internal static string WritePdf(TMConcept.Core.Paths.IAppPaths paths, string name, int pages, Func<int, bool> text, int lines = 1)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var png = Png(96, 96);
        for (var page = 1; page <= pages; page++)
        {
            var added = builder.AddPage(PageSize.A4);
            if (text(page))
            {
                for (var line = 0; line < lines; line++)
                {
                    var words = line == 0 ? Line(page) : "Line " + line + " of " + Line(page) + " The merchants paid in silver and the emperor built harbours with it.";
                    added.AddText(words, 8, new PdfPoint(30, 780 - (line * 12)), font);
                }
            }
            else
            {
                added.AddPng(png, new PdfRectangle(40, 400, 400, 760));
            }
        }

        Directory.CreateDirectory(paths.DataDirectory);
        var file = Path.Combine(paths.DataDirectory, name);
        File.WriteAllBytes(file, builder.Build());
        return file;
    }

    /// <summary>
    /// The text line of page <paramref name="page"/>: different on every page (a line that repeats on most pages, digits aside, is a running
    /// header and is left out).
    /// </summary>
    internal static string Line(int page)
    {
        var letters = string.Empty;
        for (var n = page; n > 0; n /= 26)
        {
            letters = (char)('a' + (n % 26)) + letters;
        }

        return "Page " + page + " (" + letters + ") tells of the walls.";
    }

    /// <summary>A small RGB PNG (a grey square with a dark band), written by hand: no image library in the tests.</summary>
    internal static byte[] Png(int width, int height)
    {
        var raw = new byte[height * ((width * 3) + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * ((width * 3) + 1);
            for (var x = 0; x < width * 3; x++)
            {
                raw[row + 1 + x] = (byte)(y is > 40 and < 56 ? 20 : 200);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typed);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        png.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Reports at once (a Progress would post later).</summary>
    internal sealed class Sync<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>A text-extraction engine whose answer the test gives (it may throw).</summary>
    internal sealed class FakeRecognizer(Func<PageImage, string> read) : PageTextRecognizerBase(NullLogger.Instance)
    {
        public List<int> Pages { get; } = [];

        public List<TMConcept.Core.Chat.ImagePart> Images { get; } = [];

        protected override Task<string> ReadAsync(PageImage page, CancellationToken cancellationToken)
        {
            Pages.Add(page.Page);
            Images.AddRange(page.Images);
            return Task.FromResult(read(page));
        }
    }
}
