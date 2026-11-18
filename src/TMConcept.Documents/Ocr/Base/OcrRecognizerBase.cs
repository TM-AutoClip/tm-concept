using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The shape of a local OCR engine for scanned pages (0.1.24, SKELETON: no engine derives from it yet). A page's images are decoded
/// (<see cref="IPageImageDecoder"/>), the lines of text are found (<see cref="Detect"/>), read one by one (<see cref="Recognize"/>) and put
/// in reading order: top to bottom, and left to right within a row. What is missing for a working engine: the ONNX detector (DB: a
/// probability map, thresholded, its connected regions turned into boxes and widened) and recogniser (a line scaled to
/// <see cref="LocalOcrModels.RecognizerHeight"/> pixels, its CTC output decoded greedily with the model's character list), the component that
/// downloads the two models after a confirmation with their SHA-256, the Skia decoder in the app, and the choice of the engine in the page's
/// text extraction picker. Failures and counting come from <see cref="PageTextRecognizerBase"/> like for a vision model.
/// </summary>
public abstract class OcrRecognizerBase(IPageImageDecoder decoder, ILogger logger) : PageTextRecognizerBase(logger)
{
    /// <summary>Lines whose tops are closer than this share of a line's height are one row (read left to right).</summary>
    public const double SameRowShare = 0.5;

    /// <summary>Boxes in reading order: rows top to bottom, each row left to right.</summary>
    public static IReadOnlyList<OcrLineBox> ReadingOrder(IReadOnlyList<OcrLineBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        var rows = new List<List<OcrLineBox>>();
        foreach (var box in boxes.OrderBy(b => b.Y).ThenBy(b => b.X))
        {
            var row = rows.LastOrDefault();
            if (row is not null && Math.Abs(box.Y - row[0].Y) < row[0].Height * SameRowShare)
            {
                row.Add(box);
            }
            else
            {
                rows.Add([box]);
            }
        }

        return rows.SelectMany(r => r.OrderBy(b => b.X)).ToList();
    }

    /// <inheritdoc />
    protected override Task<string> ReadAsync(PageImage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var lines = new List<string>();
        foreach (var part in page.Images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = decoder.Decode(part);
            foreach (var box in ReadingOrder(Detect(image)))
            {
                var line = Recognize(image, box).Trim();
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }
        }

        return Task.FromResult(string.Join('\n', lines));
    }

    /// <summary>The lines of text on <paramref name="image"/> (any order).</summary>
    protected abstract IReadOnlyList<OcrLineBox> Detect(DecodedImage image);

    /// <summary>The text of the line in <paramref name="box"/> (empty when nothing is read).</summary>
    protected abstract string Recognize(DecodedImage image, OcrLineBox box);
}
