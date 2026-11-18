namespace TMConcept.Documents.Ocr;

/// <summary>
/// The line recogniser's arithmetic (PP-OCR's CTC recogniser; 0.1.25), apart from the network so it is tested without a model. Before: the
/// line's box is cut out of the page image and scaled to <see cref="LocalOcrModels.RecognizerHeight"/> pixels high keeping its proportions
/// (at most <see cref="LocalOcrModels.RecognizerWidthMax"/> wide), padded on the right to at least
/// <see cref="LocalOcrModels.RecognizerWidthMin"/>, as blue, green, red values in −1…1. After: greedy CTC decoding of the [1, steps, classes]
/// probabilities: the likeliest class of each step, repeats and blanks dropped, classes turned into characters by the
/// <see cref="OcrDictionary"/>; a line whose kept characters are on average less likely than <see cref="ScoreMin"/> is noise (empty).
/// </summary>
public static class CtcLineReader
{
    /// <summary>The mean probability of a line's characters below which the line is dropped (PaddleOCR's <c>text_score</c>).</summary>
    public const float ScoreMin = 0.5f;

    /// <summary>
    /// The share of a step's probability the dotless ı needs, when the dotted i is likelier, to be read as ı. The multilingual recogniser
    /// leans to i (most of its Latin languages have no ı): measured on Turkish print, a real ı still gets 0.03–0.24 next to i, a real i
    /// gets about 0.000 for ı (18 Nov 2026).
    /// </summary>
    public const float DotlessShare = 0.02f;

    /// <summary>The share of a step's probability the dotted capital İ needs, when I is likelier, to be read as İ (measured likewise).</summary>
    public const float DottedCapitalShare = 0.05f;

    /// <summary>The recogniser's input for the line in <paramref name="box"/> of <paramref name="image"/>.</summary>
    public static OcrTensor Prepare(DecodedImage image, OcrLineBox box)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(box);
        var height = LocalOcrModels.RecognizerHeight;
        var content = Math.Clamp((int)Math.Ceiling(height * box.Width / (double)Math.Max(1, box.Height)), 1, LocalOcrModels.RecognizerWidthMax);
        var width = Math.Max(LocalOcrModels.RecognizerWidthMin, content);
        var data = new float[3 * height * width];
        var plane = height * width;
        var stepX = box.Width / (double)content;
        var stepY = box.Height / (double)height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < content; x++)
            {
                var (red, green, blue) = OcrPixels.Mean(image, box.X + (x * stepX), box.Y + (y * stepY), box.X + ((x + 1) * stepX), box.Y + ((y + 1) * stepY));
                var target = (y * width) + x;
                data[target] = (blue / 127.5f) - 1f;
                data[plane + target] = (green / 127.5f) - 1f;
                data[(2 * plane) + target] = (red / 127.5f) - 1f;
            }
        }

        return new OcrTensor(data, [1, 3, height, width]);
    }

    /// <summary>The text of the recogniser's <paramref name="output"/>; <see cref="InvalidDataException"/> when its classes do not fit <paramref name="dictionary"/>.</summary>
    public static string Decode(OcrTensor output, OcrDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(dictionary);
        if (output.Shape.Length < 2)
        {
            throw new InvalidDataException("The OCR recogniser gave an output of an unknown shape.");
        }

        var classes = output.Shape[^1];
        var steps = output.Shape[^2];
        if (classes != dictionary.Classes && classes != dictionary.Classes - 1)
        {
            throw new InvalidDataException($"The OCR recogniser reads {classes} classes; its character list has {dictionary.Classes}.");
        }

        if ((long)steps * classes > output.Data.Length)
        {
            throw new InvalidDataException("The OCR recogniser gave fewer values than its shape says.");
        }

        var text = new System.Text.StringBuilder();
        var previous = 0;
        var score = 0.0;
        var kept = 0;
        for (var step = 0; step < steps; step++)
        {
            var row = output.Data.AsSpan(step * classes, classes);
            var best = 0;
            for (var c = 1; c < classes; c++)
            {
                if (row[c] > row[best])
                {
                    best = c;
                }
            }

            // The step's certainty is its likeliest class's, also when the Turkish letter of the pair is taken.
            var certainty = row[best];
            best = Turkish(row, best, dictionary);
            if (best != 0 && best != previous)
            {
                text.Append(dictionary.Text(best));
                score += certainty;
                kept++;
            }

            previous = best;
        }

        return kept == 0 || score / kept < ScoreMin ? string.Empty : text.ToString().Trim();
    }

    /// <summary>The Turkish letter of the pairs i/ı and I/İ when the recogniser's lean to the dotted (or plain) one is not sure (see <see cref="DotlessShare"/>).</summary>
    private static int Turkish(ReadOnlySpan<float> row, int best, OcrDictionary dictionary)
    {
        if (best == dictionary.SmallI && dictionary.DotlessI > 0 && row[dictionary.DotlessI] >= DotlessShare)
        {
            return dictionary.DotlessI;
        }

        return best == dictionary.CapitalI && dictionary.DottedCapitalI > 0 && row[dictionary.DottedCapitalI] >= DottedCapitalShare ? dictionary.DottedCapitalI : best;
    }
}
