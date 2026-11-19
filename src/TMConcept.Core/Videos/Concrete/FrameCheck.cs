using TMConcept.Core.Media;

namespace TMConcept.Core.Videos;

/// <summary>
/// The content check of a rendered video (20 Nov 2026; a video was delivered as "ready" that showed only a coloured background for 95 s,
/// although its scene image had been made). One frame from the middle of each scene (<see cref="IMediaTools.SampleAsync"/>, the same small
/// RGB copies the readable-text measure uses) is turned into a coarse grey grid: a near-uniform frame fails, and with a scene image the
/// frame must resemble the image (the best Pearson correlation of the grids over a few framings: the whole image, the image zoomed in or
/// mid-pan, the image in a smaller box in the middle, and since the 0.1.30 review also the image in one half of the frame, the top or bottom
/// half of a vertical video with a text card in the other, or a left/right split; until then a good online design could fail). A grey grid
/// is cheap and blind to colour changes, so a tinted or darkened image still passes. Pure functions; the caller samples and decides.
/// </summary>
public static class FrameCheck
{
    /// <summary>
    /// The framings tried: the part of the frame and the part of the image that are compared (left, top, width, height as shares). The samples
    /// are fixed-size grids and the images are made at the video's size, so a half of the frame shows the middle half of the image when it
    /// covers its box (object-fit: cover).
    /// </summary>
    private static readonly ((double X, double Y, double W, double H) Frame, (double X, double Y, double W, double H) Image)[] Framings =
    [
        ((0, 0, 1, 1), (0, 0, 1, 1)),
        ((0, 0, 1, 1), (0.05, 0.05, 0.9, 0.9)),
        ((0, 0, 1, 1), (0.1, 0.1, 0.8, 0.8)),
        ((0, 0, 1, 1), (0, 0, 0.8, 0.8)),
        ((0, 0, 1, 1), (0.2, 0, 0.8, 0.8)),
        ((0, 0, 1, 1), (0, 0.2, 0.8, 0.8)),
        ((0, 0, 1, 1), (0.2, 0.2, 0.8, 0.8)),
        ((0.1, 0.1, 0.8, 0.8), (0, 0, 1, 1)),
        ((0.2, 0.2, 0.6, 0.6), (0, 0, 1, 1)),
        ((0, 0, 1, 0.5), (0, 0.25, 1, 0.5)),
        ((0, 0.5, 1, 0.5), (0, 0.25, 1, 0.5)),
        ((0, 0, 0.5, 1), (0.25, 0, 0.5, 1)),
        ((0.5, 0, 0.5, 1), (0.25, 0, 0.5, 1)),
    ];

    /// <summary>
    /// The score of one scene: <paramref name="frame"/> from its middle and, when it has one, its <paramref name="image"/> (null = no image, or
    /// the image could not be read).
    /// </summary>
    public static SceneFrameScore Score(int scene, FramePixels frame, FramePixels? image)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var deviation = Deviation(frame);

        // A plain image (a dark sky, an empty page) makes a plain frame: neither test can tell it from an empty one, so it passes.
        if (image is not null && Deviation(image) < FrameCheckLimits.UniformDeviation)
        {
            return new SceneFrameScore(scene, deviation, null, false);
        }

        var uniform = deviation < FrameCheckLimits.UniformDeviation;
        double? resemblance = image is null ? null : uniform ? 0 : Resemblance(frame, image);
        return new SceneFrameScore(scene, deviation, resemblance, uniform);
    }

    /// <summary>The standard deviation of the brightness (0–255) of every pixel.</summary>
    public static double Deviation(FramePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var rgb = pixels.Rgb.Span;
        var count = pixels.Width * pixels.Height;
        if (count == 0 || rgb.Length < count * 3)
        {
            return 0;
        }

        double sum = 0, squares = 0;
        for (var i = 0; i < count; i++)
        {
            var grey = Grey(rgb, i * 3);
            sum += grey;
            squares += grey * grey;
        }

        var mean = sum / count;
        return Math.Sqrt(Math.Max(0, (squares / count) - (mean * mean)));
    }

    /// <summary>The best correlation (−1…1) of the frame's and the image's grey grids over the framings.</summary>
    public static double Resemblance(FramePixels frame, FramePixels image)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(image);
        return Framings.Max(f => Correlation(Grid(frame, f.Frame), Grid(image, f.Image)));
    }

    /// <summary>The mean grey of each cell of a part of the picture (<paramref name="box"/>, shares of width and height) on the check's grid.</summary>
    private static double[] Grid(FramePixels pixels, (double X, double Y, double W, double H) box)
    {
        var rgb = pixels.Rgb.Span;
        var width = FrameCheckLimits.GridWidth;
        var height = FrameCheckLimits.GridHeight;
        var cells = new double[width * height];
        if (pixels.Width == 0 || pixels.Height == 0 || rgb.Length < pixels.Width * pixels.Height * 3)
        {
            return cells;
        }

        var left = pixels.Width * box.X;
        var top = pixels.Height * box.Y;
        var cropWidth = pixels.Width * box.W;
        var cropHeight = pixels.Height * box.H;
        for (var gy = 0; gy < height; gy++)
        {
            var y0 = Math.Clamp((int)Math.Floor(top + (cropHeight * gy / height)), 0, pixels.Height - 1);
            var y1 = Math.Clamp((int)Math.Ceiling(top + (cropHeight * (gy + 1) / height)), y0 + 1, pixels.Height);
            for (var gx = 0; gx < width; gx++)
            {
                var x0 = Math.Clamp((int)Math.Floor(left + (cropWidth * gx / width)), 0, pixels.Width - 1);
                var x1 = Math.Clamp((int)Math.Ceiling(left + (cropWidth * (gx + 1) / width)), x0 + 1, pixels.Width);
                double sum = 0;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        sum += Grey(rgb, ((y * pixels.Width) + x) * 3);
                    }
                }

                cells[(gy * width) + gx] = sum / ((y1 - y0) * (x1 - x0));
            }
        }

        return cells;
    }

    /// <summary>Pearson correlation; 0 when either side is flat (nothing to compare).</summary>
    private static double Correlation(double[] a, double[] b)
    {
        var meanA = a.Average();
        var meanB = b.Average();
        double cross = 0, varA = 0, varB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var da = a[i] - meanA;
            var db = b[i] - meanB;
            cross += da * db;
            varA += da * da;
            varB += db * db;
        }

        return varA < 1e-6 || varB < 1e-6 ? 0 : cross / Math.Sqrt(varA * varB);
    }

    private static double Grey(ReadOnlySpan<byte> rgb, int at) => (0.299 * rgb[at]) + (0.587 * rgb[at + 1]) + (0.114 * rgb[at + 2]);
}
