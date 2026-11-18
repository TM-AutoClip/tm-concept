namespace TMConcept.Documents.Ocr;

/// <summary>
/// The text line detector's arithmetic (PP-OCR's DB, "differentiable binarization"; 0.1.25), apart from the network so it is tested without a
/// model. Before: the page image is scaled so its longer side is at most <see cref="LocalOcrModels.DetectorSideMax"/> (never larger; each
/// side a multiple of <see cref="LocalOcrModels.DetectorStride"/>) into a [1, 3, h, w] tensor of blue, green, red (PaddleOCR reads images as
/// BGR) normalised with the ImageNet mean and deviation. After: the network's probability map is cut at <see cref="Threshold"/>, each
/// connected region (4-neighbours) is one line, kept when its mean probability reaches <see cref="BoxThreshold"/> and it is at least
/// <see cref="MinSide"/> pixels on both sides; its upright box is widened like PaddleOCR's "unclip" (by area × <see cref="UnclipRatio"/> /
/// perimeter) and taken back to the page image's pixels.
/// </summary>
public static class DbTextDetector
{
    /// <summary>A map pixel above this is text.</summary>
    public const float Threshold = 0.3f;

    /// <summary>The mean probability a region needs to be a line.</summary>
    public const float BoxThreshold = 0.5f;

    /// <summary>How far a region's box is widened (PaddleOCR's <c>unclip_ratio</c>).</summary>
    public const double UnclipRatio = 1.5;

    /// <summary>A region narrower or lower than this (map pixels) is noise.</summary>
    public const int MinSide = 3;

    /// <summary>At most this many lines on one image (a page of noise does not run for ever).</summary>
    public const int LinesMax = 3_000;

    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Deviation = [0.229f, 0.224f, 0.225f];

    /// <summary>The detector's input for <paramref name="image"/> and the scale of each axis (input pixels per image pixel).</summary>
    public static (OcrTensor Input, double ScaleX, double ScaleY) Prepare(DecodedImage image, int sideMax)
    {
        ArgumentNullException.ThrowIfNull(image);
        var scale = Math.Min(1.0, sideMax / (double)Math.Max(image.Width, image.Height));
        var width = Round(image.Width * scale);
        var height = Round(image.Height * scale);
        var data = new float[3 * width * height];
        var plane = width * height;
        var stepX = image.Width / (double)width;
        var stepY = image.Height / (double)height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (red, green, blue) = OcrPixels.Mean(image, x * stepX, y * stepY, (x + 1) * stepX, (y + 1) * stepY);
                var target = (y * width) + x;

                // Blue, green, red: the channel order the PaddleOCR models were trained with.
                data[target] = ((blue / 255f) - Mean[0]) / Deviation[0];
                data[plane + target] = ((green / 255f) - Mean[1]) / Deviation[1];
                data[(2 * plane) + target] = ((red / 255f) - Mean[2]) / Deviation[2];
            }
        }

        return (new OcrTensor(data, [1, 3, height, width]), width / (double)image.Width, height / (double)image.Height);
    }

    /// <summary>The lines on the probability <paramref name="map"/> in the page image's pixels (<paramref name="width"/> × <paramref name="height"/>).</summary>
    public static IReadOnlyList<OcrLineBox> Boxes(OcrTensor map, double scaleX, double scaleY, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Shape.Length < 2)
        {
            throw new InvalidDataException("The OCR detector gave an output of an unknown shape.");
        }

        var mapHeight = map.Shape[^2];
        var mapWidth = map.Shape[^1];
        if ((long)mapHeight * mapWidth > map.Data.Length)
        {
            throw new InvalidDataException("The OCR detector gave fewer values than its shape says.");
        }

        var probabilities = map.Data;
        var labels = new int[mapWidth * mapHeight];
        var stack = new Stack<int>();
        var boxes = new List<OcrLineBox>();
        var region = 0;
        for (var start = 0; start < labels.Length && boxes.Count < LinesMax; start++)
        {
            if (labels[start] != 0 || probabilities[start] <= Threshold)
            {
                continue;
            }

            region++;
            labels[start] = region;
            stack.Push(start);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            var sum = 0.0;
            var count = 0;
            while (stack.Count > 0)
            {
                var at = stack.Pop();
                var x = at % mapWidth;
                var y = at / mapWidth;
                (minX, maxX, minY, maxY) = (Math.Min(minX, x), Math.Max(maxX, x), Math.Min(minY, y), Math.Max(maxY, y));
                sum += probabilities[at];
                count++;
                Visit(x > 0 ? at - 1 : -1);
                Visit(x < mapWidth - 1 ? at + 1 : -1);
                Visit(y > 0 ? at - mapWidth : -1);
                Visit(y < mapHeight - 1 ? at + mapWidth : -1);
            }

            var boxWidth = maxX - minX + 1;
            var boxHeight = maxY - minY + 1;
            if (Math.Min(boxWidth, boxHeight) < MinSide || sum / count < BoxThreshold)
            {
                continue;
            }

            var grow = boxWidth * boxHeight * UnclipRatio / (2.0 * (boxWidth + boxHeight));
            var left = Math.Clamp((int)Math.Floor((minX - grow) / scaleX), 0, width - 1);
            var top = Math.Clamp((int)Math.Floor((minY - grow) / scaleY), 0, height - 1);
            var right = Math.Clamp((int)Math.Ceiling((maxX + 1 + grow) / scaleX), left + 1, width);
            var bottom = Math.Clamp((int)Math.Ceiling((maxY + 1 + grow) / scaleY), top + 1, height);
            if (Math.Min(right - left, bottom - top) >= MinSide)
            {
                boxes.Add(new OcrLineBox(left, top, right - left, bottom - top));
            }
        }

        return boxes;

        void Visit(int next)
        {
            if (next >= 0 && labels[next] == 0 && probabilities[next] > Threshold)
            {
                labels[next] = region;
                stack.Push(next);
            }
        }
    }

    private static int Round(double side) => Math.Max(LocalOcrModels.DetectorStride, (int)Math.Round(side / LocalOcrModels.DetectorStride) * LocalOcrModels.DetectorStride);
}
