using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// A local OCR engine for scanned pages (0.1.24 shape, 0.1.25 working: <see cref="PaddleOcrRecognizer"/>). A page's images are decoded
/// (<see cref="IPageImageDecoder"/>; an image that cannot be decoded is skipped and logged, a page none of whose images can be decoded fails),
/// the lines of text are found (<see cref="Detect"/>), put in reading order (<see cref="ReadingOrder(IReadOnlyList{OcrLineBox}, int)"/>: two
/// columns are read one after the other) and read one by one (<see cref="Recognize"/>); a cancel stops between lines. Failures and counting
/// come from <see cref="PageTextRecognizerBase"/> like for a vision model.
/// </summary>
public abstract class OcrRecognizerBase(IPageImageDecoder decoder, ILogger logger) : PageTextRecognizerBase(logger)
{
    /// <summary>Lines whose tops are closer than this share of a line's height are one row (read left to right).</summary>
    public const double SameRowShare = 0.5;

    /// <summary>A line wider than this share of the image spans the columns (a title, a heading across the page).</summary>
    public const double SpanningShare = 0.45;

    /// <summary>Lines each column needs before the page counts as two columns.</summary>
    public const int ColumnLinesMin = 3;

    /// <summary>Boxes in reading order, one column: rows top to bottom, each row left to right.</summary>
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

    /// <summary>
    /// Boxes in reading order on an image <paramref name="width"/> pixels wide: when an empty vertical band (the gutter) parts the lines in the
    /// middle half of the page into two columns of at least <see cref="ColumnLinesMin"/> lines, each stretch between lines that span the page
    /// is read left column first, then the right one; otherwise <see cref="ReadingOrder(IReadOnlyList{OcrLineBox})"/>.
    /// </summary>
    public static IReadOnlyList<OcrLineBox> ReadingOrder(IReadOnlyList<OcrLineBox> boxes, int width)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        if (width <= 0 || Gutter(boxes, width) is not { } gutter)
        {
            return ReadingOrder(boxes);
        }

        var spanning = boxes.Where(b => b.X < gutter.To && b.X + b.Width > gutter.From).OrderBy(b => b.Y).ToList();
        var order = new List<OcrLineBox>();
        var top = int.MinValue;
        foreach (var limit in spanning.Select(b => (OcrLineBox?)b).Append(null))
        {
            var bottom = limit?.Y ?? int.MaxValue;
            var stretch = boxes.Where(b => !spanning.Contains(b) && b.Y >= top && b.Y < bottom).ToList();
            order.AddRange(ReadingOrder(stretch.Where(b => b.X + b.Width <= gutter.From).ToList()));
            order.AddRange(ReadingOrder(stretch.Where(b => b.X >= gutter.To).ToList()));
            if (limit is { } line)
            {
                order.Add(line);
                top = line.Y;
            }
        }

        return order;
    }

    /// <inheritdoc />
    protected override Task<string> ReadAsync(PageImage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var lines = new List<string>();
        var decoded = 0;
        Exception? failure = null;
        foreach (var part in page.Images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DecodedImage image;
            try
            {
                image = decoder.Decode(part);
            }
            catch (InvalidDataException ex)
            {
                // Another image of the page may still be read; the page fails only when none can be.
                Logger.LogWarning("An image ({Type}) of page {Page} could not be decoded for OCR: {Reason}", part.MimeType, page.Page, ex.Message);
                failure = ex;
                continue;
            }

            decoded++;
            foreach (var box in ReadingOrder(Detect(image), image.Width))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = Recognize(image, box).Trim();
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }
        }

        if (decoded == 0 && failure is not null)
        {
            throw new InvalidDataException("No image of page " + page.Page + " could be decoded: " + failure.Message, failure);
        }

        return Task.FromResult(string.Join('\n', lines));
    }

    /// <summary>The logger of the engine.</summary>
    protected ILogger Logger { get; } = logger;

    /// <summary>The lines of text on <paramref name="image"/> (any order).</summary>
    protected abstract IReadOnlyList<OcrLineBox> Detect(DecodedImage image);

    /// <summary>The text of the line in <paramref name="box"/> (empty when nothing is read).</summary>
    protected abstract string Recognize(DecodedImage image, OcrLineBox box);

    /// <summary>The widest empty vertical band in the middle half of the page between two columns of lines, or null.</summary>
    private static (int From, int To)? Gutter(IReadOnlyList<OcrLineBox> boxes, int width)
    {
        var narrow = boxes.Where(b => b.Width < width * SpanningShare).ToList();
        if (narrow.Count < ColumnLinesMin * 2)
        {
            return null;
        }

        var covered = new bool[width + 1];
        foreach (var box in narrow)
        {
            for (var x = Math.Max(0, box.X); x < Math.Min(width, box.X + box.Width); x++)
            {
                covered[x] = true;
            }
        }

        (int From, int To)? best = null;
        var start = -1;
        for (var x = width / 4; x <= (width * 3) / 4; x++)
        {
            if (!covered[x])
            {
                start = start < 0 ? x : start;
                continue;
            }

            if (start >= 0 && (best is null || x - start > best.Value.To - best.Value.From))
            {
                best = (start, x);
            }

            start = -1;
        }

        if (best is not { } gutter || gutter.To - gutter.From < Math.Max(2, width / 100))
        {
            return null;
        }

        var left = narrow.Count(b => b.X + b.Width <= gutter.From);
        var right = narrow.Count(b => b.X >= gutter.To);
        return left >= ColumnLinesMin && right >= ColumnLinesMin ? gutter : null;
    }
}
