using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Documents;
using TMConcept.Core.Models;
using TMConcept.Core.Tools;
using TMConcept.Providers.Catalog;
using TMConcept.Desktop.Documents;
using TMConcept.Documents.Extractors;
using TMConcept.Documents.Ocr;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Documents;

/// <summary>
/// 0.1.25, the local OCR of scanned pages: the bundled character list has the Turkish letters, the detector's and recogniser's arithmetic,
/// the reading order of one and two columns, the image decoder, and the whole pipeline on a tiny generated image and on a scanned PDF page
/// with FAKE networks (no model file, no network). The pinned model files and the component are checked without downloading anything.
/// </summary>
public sealed class LocalOcrTests
{
    private static readonly OcrDictionary Latin = OcrDictionary.Latin();

    [Fact]
    public void TheBundledCharacterListHasEveryTurkishLetter()
    {
        foreach (var letter in new[] { "ç", "ğ", "ı", "İ", "ö", "ş", "ü", "Ç", "Ğ", "Ö", "Ş", "Ü", "â", "î", "û", "i", "I" })
        {
            Latin.Characters.ShouldContain(letter, "the Latin recogniser must read " + letter);
        }

        // The recogniser gives 504 classes: the blank, the 502 characters and the space.
        Latin.Characters.Count.ShouldBe(502);
        Latin.Classes.ShouldBe(504);
        Latin.Text(0).ShouldBe(string.Empty);
        Latin.Text(503).ShouldBe(" ");
        Latin.Text(Latin.Characters.ToList().IndexOf("ş") + 1).ShouldBe("ş");
    }

    [Fact]
    public void GreedyCtcDropsBlanksAndRepeatsButKeepsDoubledLetters()
    {
        Decode("Allah'ın").ShouldBe("Allah'ın");
        Decode("Şişli çarşı").ShouldBe("Şişli çarşı");

        // An unsure line is noise; a recogniser of another character list is refused.
        var unsure = Output("Ağrı", 0.3f);
        CtcLineReader.Decode(unsure, Latin).ShouldBe(string.Empty);
        Should.Throw<InvalidDataException>(() => CtcLineReader.Decode(new OcrTensor(new float[10 * 97], [1, 10, 97]), Latin));
    }

    [Fact]
    public void AnUnsureDottedIIsReadAsTheTurkishDotlessOne()
    {
        // Measured on Turkish print (18 Nov 2026): a real ı keeps a few hundredths for ı next to i; a real i keeps none.
        var i = Latin.Class("i");
        var dotless = Latin.Class("ı");
        var capital = Latin.Class("I");
        var dotted = Latin.Class("İ");
        var data = new float[4 * Latin.Classes];
        (data[i], data[dotless]) = (0.9f, 0.08f);
        data[Latin.Classes] = 1f;
        (data[(2 * Latin.Classes) + i], data[(2 * Latin.Classes) + dotless]) = (0.99f, 0.001f);
        (data[(3 * Latin.Classes) + capital], data[(3 * Latin.Classes) + dotted]) = (0.86f, 0.13f);

        CtcLineReader.Decode(new OcrTensor(data, [1, 4, Latin.Classes]), Latin).ShouldBe("ıiİ");
    }

    [Fact]
    public void TheDetectorScalesThePageAndTurnsItsMapIntoWidenedBoxes()
    {
        var page = Page(3_200, 1_000, (100, 200, 2_000, 60));
        var (input, scaleX, scaleY) = DbTextDetector.Prepare(page, LocalOcrModels.DetectorSideMax);

        input.Shape.ShouldBe([1, 3, 512, 1_600]);
        (scaleX, scaleY).ShouldBe((0.5, 0.512));
        var map = new FakeDetector().Run(input);
        var boxes = DbTextDetector.Boxes(map, scaleX, scaleY, page.Width, page.Height);

        var box = boxes.ShouldHaveSingleItem();
        box.X.ShouldBeLessThan(100);
        box.Y.ShouldBeLessThan(200);
        (box.X + box.Width).ShouldBeGreaterThan(2_100);
        (box.Y + box.Height).ShouldBeGreaterThan(260);
        (box.Y + box.Height).ShouldBeLessThan(330, "widened like PaddleOCR's unclip, not swallowing the page");
    }

    [Fact]
    public void TheRecognizerGetsTheLineAtItsHeightPaddedToItsWidth()
    {
        var page = Page(400, 200, (20, 30, 280, 14));
        var input = CtcLineReader.Prepare(page, new OcrLineBox(10, 20, 300, 30));
        input.Shape.ShouldBe([1, 3, 48, 480]);
        var shortWord = CtcLineReader.Prepare(page, new OcrLineBox(10, 20, 30, 30));
        shortWord.Shape.ShouldBe([1, 3, 48, 320]);
        shortWord.Data.Max().ShouldBeLessThanOrEqualTo(1f);
        shortWord.Data.Min().ShouldBeGreaterThanOrEqualTo(-1f);
    }

    [Fact]
    public void TwoColumnsAreReadOneAfterTheOtherWithTheTitleFirst()
    {
        OcrLineBox[] boxes =
        [
            new(520, 100, 400, 20), new(40, 100, 400, 20), new(40, 140, 400, 20), new(520, 140, 400, 20), new(40, 180, 400, 20),
            new(520, 180, 400, 20), new(40, 20, 880, 30),
        ];

        var order = OcrRecognizerBase.ReadingOrder(boxes, 960);

        order.Select(b => (b.X, b.Y)).ShouldBe([(40, 20), (40, 100), (40, 140), (40, 180), (520, 100), (520, 140), (520, 180)]);
        OcrRecognizerBase.ReadingOrder(boxes.Take(2).ToList(), 960).Select(b => b.X).ShouldBe([40, 520], "one row of a page without columns");
    }

    [Fact]
    public async Task TheEngineReadsATinyGeneratedImageWithFakeNetworksInReadingOrder()
    {
        var page = Page(400, 200, (20, 100, 230, 14), (20, 30, 280, 14));
        var recognizer = new FakeRecognizer("İstanbul", "Şişli çarşı");
        using var engine = new PaddleOcrRecognizer(new FixedDecoder(page), new FakeDetector(), recognizer, Latin, NullLogger.Instance);

        var read = await engine.RecognizeAsync(new PageImage(1, [new ImagePart([1, 2, 3], "image/png")]), CancellationToken.None);

        read.ShouldBe(new PageRecognition("İstanbul\nŞişli çarşı", false));
        recognizer.Inputs.Select(i => i[2]).ShouldBe([48, 48]);
        engine.Recognized.ShouldBe(1);
    }

    [Fact]
    public async Task AScannedPdfPageIsDecodedBySkiaAndReadByTheLocalOcr()
    {
        using var paths = new TempPaths();
        var file = LargePdfTests.WritePdf(paths, "scan.pdf", 3, page => page != 2);
        using var engine = new PaddleOcrRecognizer(new SkiaPageImageDecoder(), new FakeDetector(), new FakeRecognizer("Ayasofya"), Latin, NullLogger.Instance);

        var read = await new PdfExtractor().ExtractAsync(file, new DocumentReadOptions(Recognizer: engine), CancellationToken.None);

        (read.PagesRecognized, read.PagesWithoutText, read.PagesUnread).ShouldBe((1, 0, 0));
        read.Sections.Single(s => s.Page == 2).Text.ShouldBe("Ayasofya");
    }

    [Fact]
    public void SkiaDecodesThePngAPdfHandsOnAndRefusesWhatIsNoImage()
    {
        var image = new SkiaPageImageDecoder().Decode(new ImagePart(LargePdfTests.Png(96, 96), "image/png"));

        (image.Width, image.Height).ShouldBe((96, 96));
        image.Rgb[0].ShouldBe((byte)200);
        image.Rgb[((48 * 96) + 10) * 3].ShouldBe((byte)20, "the dark band");
        Should.Throw<InvalidDataException>(() => new SkiaPageImageDecoder().Decode(new ImagePart([0x00, 0x01, 0x02, 0x03], "image/jp2")));
    }

    [Fact]
    public async Task APageWhoseImagesCannotBeDecodedFailsAndIsCounted()
    {
        using var engine = new PaddleOcrRecognizer(new SkiaPageImageDecoder(), new FakeDetector(), new FakeRecognizer(), Latin, NullLogger.Instance);

        var read = await engine.RecognizeAsync(new PageImage(4, [new ImagePart([0, 1, 2, 3], "image/png")]), CancellationToken.None);

        read.Failed.ShouldBeTrue();
        engine.Failed.ShouldBe(1);
    }

    [Fact]
    public void TheModelFilesArePinnedAndComeFromAnAllowedHost()
    {
        LocalOcrModels.Files.Count.ShouldBe(2);
        foreach (var file in LocalOcrModels.Files)
        {
            file.Sha256.Length.ShouldBe(64);
            file.Sha256.ShouldAllBe(c => char.IsAsciiHexDigitLower(c));
            file.Url.Scheme.ShouldBe("https");
            ToolManifest.AllowedHosts.ShouldContain(file.Url.Host);
            file.ApproximateBytes.ShouldBeGreaterThan(1_000_000);
            file.Tool.ShouldBe(ToolKind.OcrWeights);
        }
    }

    [Fact]
    public void TheComponentListsItsDownloadsWithSizesAndTheEngineNeedsThemInstalled()
    {
        using var paths = new TempPaths();
        var engine = new LocalOcrEngine(paths, new RefusingDownloader(), new SkiaPageImageDecoder(), NullLoggerFactory.Instance);
        var component = new LocalOcrComponentSource(engine).Components.ShouldHaveSingleItem();

        component.Code.ShouldBe(PageTextEngines.LocalOcrComponent);
        component.Ready.ShouldBeFalse();
        component.Downloads().Select(d => d.ApproximateBytes).ShouldBe([4_819_576L, 7_904_513L]);
        component.Downloads().ShouldAllBe(d => d.Host == "www.modelscope.cn");
        Should.Throw<InvalidOperationException>(() => engine.Create()).Message.ShouldContain("not installed");
    }

    [Fact]
    public void TheLocalOcrIsInTheCatalogFreeAndItsEstimateIsTimeOnly()
    {
        var model = new EmbeddedModelCatalog().All.Single(m => PageTextEngines.IsLocalOcr(m));
        model.Requires.ShouldBe([PageTextEngines.LocalOcrComponent]);
        model.Capabilities.HasFlag(ModelCapability.Vision).ShouldBeTrue();

        var reader = new DocumentTextReader(new LargeDocumentTests.EveryCallChat(_ => string.Empty), new CostCalculator(), new LargeDocumentTests.NoRuns(),
            TimeProvider.System, NullLogger<DocumentTextReader>.Instance);
        var estimate = reader.Estimate(model, 700, 0);

        estimate.Cost.MaxUsd.ShouldBe(0m);
        estimate.Local.ShouldBeTrue();
        estimate.TimeMax.ShouldBeGreaterThan(estimate.TimeMin);
        PageTextEngines.OcrThreads(2).ShouldBe(1);
        PageTextEngines.OcrThreads(64).ShouldBe(4);
    }

    /// <summary>A white page of <paramref name="width"/> × <paramref name="height"/> with black bars (lines of "text").</summary>
    internal static DecodedImage Page(int width, int height, params (int X, int Y, int Width, int Height)[] bars)
    {
        var rgb = new byte[width * height * 3];
        Array.Fill(rgb, (byte)255);
        foreach (var (x0, y0, w, h) in bars)
        {
            for (var y = y0; y < y0 + h; y++)
            {
                Array.Fill(rgb, (byte)0, ((y * width) + x0) * 3, w * 3);
            }
        }

        return new DecodedImage(width, height, rgb);
    }

    /// <summary>The recogniser's probabilities spelling <paramref name="text"/> (a blank between doubled letters, blanks after).</summary>
    internal static OcrTensor Output(string text, float sure = 0.99f)
    {
        var classes = new List<int>();
        foreach (var letter in text.Select(c => c.ToString()))
        {
            var index = letter == " " ? Latin.Classes - 1 : Latin.Characters.ToList().IndexOf(letter) + 1;
            index.ShouldBeGreaterThan(0, "the test spells only listed letters");
            if (classes.Count > 0 && classes[^1] == index)
            {
                classes.Add(0);
            }

            classes.Add(index);
            classes.Add(index);
        }

        classes.AddRange(Enumerable.Repeat(0, 5));
        var data = new float[classes.Count * Latin.Classes];
        for (var step = 0; step < classes.Count; step++)
        {
            data[(step * Latin.Classes) + classes[step]] = sure;
        }

        return new OcrTensor(data, [1, classes.Count, Latin.Classes]);
    }

    private static string Decode(string text) => CtcLineReader.Decode(Output(text), Latin);

    /// <summary>"Text" wherever the detector's input is dark (its blue channel below the mean).</summary>
    internal sealed class FakeDetector : IOcrNetwork
    {
        public OcrTensor Run(OcrTensor input)
        {
            var (height, width) = (input.Shape[2], input.Shape[3]);
            var map = new float[height * width];
            for (var i = 0; i < map.Length; i++)
            {
                map[i] = input.Data[i] < 0 ? 0.9f : 0.01f;
            }

            return new OcrTensor(map, [1, 1, height, width]);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Spells the given lines one call after the other (empty after the last).</summary>
    internal sealed class FakeRecognizer(params string[] lines) : IOcrNetwork
    {
        public List<int[]> Inputs { get; } = [];

        public OcrTensor Run(OcrTensor input)
        {
            Inputs.Add(input.Shape);
            return Inputs.Count <= lines.Length ? Output(lines[Inputs.Count - 1]) : new OcrTensor(new float[Latin.Classes], [1, 1, Latin.Classes]);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Gives the same decoded page for any image.</summary>
    internal sealed class FixedDecoder(DecodedImage page) : IPageImageDecoder
    {
        public DecodedImage Decode(ImagePart image) => page;
    }

    /// <summary>A downloader that must never be called.</summary>
    private sealed class RefusingDownloader : IArchiveDownloader
    {
        public Task DownloadAsync(ToolArchive archive, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Nothing is downloaded in this test.");
    }
}
