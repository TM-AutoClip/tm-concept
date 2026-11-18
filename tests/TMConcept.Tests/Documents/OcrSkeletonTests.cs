using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Documents;
using TMConcept.Documents.Extractors;
using TMConcept.Documents.Ocr;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Documents;

/// <summary>
/// 0.1.24, the shape of a local OCR engine (a skeleton: the detector and recogniser are mocked here): lines are read in reading order, Turkish
/// letters pass through, and the engine plugs into the PDF reader like a vision model.
/// </summary>
public sealed class OcrSkeletonTests
{
    [Fact]
    public void LinesAreReadTopToBottomAndLeftToRightWithinARow()
    {
        var order = OcrRecognizerBase.ReadingOrder([new(300, 102, 200, 20), new(10, 200, 200, 20), new(10, 100, 200, 20)]);

        order.Select(b => (b.X, b.Y)).ShouldBe([(10, 100), (300, 102), (10, 200)]);
    }

    [Fact]
    public async Task AMockedEngineReadsTheScannedPagesOfAPdf()
    {
        using var paths = new TempPaths();
        var file = LargePdfTests.WritePdf(paths, "scan.pdf", 2, page => page == 2);
        var engine = new MockedEngine();

        var read = await new PdfExtractor().ExtractAsync(file, new DocumentReadOptions(Recognizer: engine), CancellationToken.None);

        (read.PagesRecognized, read.PagesWithoutText).ShouldBe((1, 0));
        read.Sections[0].Text.ShouldBe("Ayasofya\nİstanbul, Şubat");
        engine.Decoded.ShouldBe(1);
    }

    /// <summary>Decodes nothing; finds two lines on a row and below; reads Turkish words.</summary>
    private sealed class MockedEngine() : OcrRecognizerBase(new Decoder(), NullLogger.Instance)
    {
        public int Decoded => Decoder.Count;

        protected override IReadOnlyList<OcrLineBox> Detect(DecodedImage image) => [new(10, 60, 80, 10), new(10, 20, 80, 10)];

        protected override string Recognize(DecodedImage image, OcrLineBox box) => box.Y == 20 ? "Ayasofya" : "İstanbul, Şubat";

        private sealed class Decoder : IPageImageDecoder
        {
            public static int Count { get; private set; }

            public DecodedImage Decode(ImagePart image)
            {
                Count++;
                return new DecodedImage(96, 96, new byte[96 * 96 * 3]);
            }
        }
    }
}
