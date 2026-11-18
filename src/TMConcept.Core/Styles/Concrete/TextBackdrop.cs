using TMConcept.Core.Media;
using TMConcept.Core.Subtitles;

namespace TMConcept.Core.Styles;

/// <summary>
/// Readable text where it matters (0.1.25, with <see cref="VideoStyle.ReadableText"/> on): what is behind text is measured (a small copy of
/// the region from ffmpeg, <see cref="IMediaTools.SampleAsync"/>) and the outline and shadow get stronger, or a subtle see-through backdrop
/// is added, where the contrast is low or the background is busy. The subtitles are measured on frames of the rendered video under their
/// own place, one sample per subtitle (at most <see cref="MaxSamples"/>); the on-screen text, whose place only the video code knows, on the
/// scene images as a whole. A region that could not be read keeps the normal readable outline.
/// </summary>
public static class TextBackdrop
{
    /// <summary>At most this many subtitles are sampled (one ffmpeg call each); the others take the need of the nearest sampled one.</summary>
    public const int MaxSamples = 24;

    /// <summary>A pixel whose contrast with the text colour is below this is "close to the text" (WCAG: 3 is the floor for large text).</summary>
    public const double LowContrast = 2.5;

    /// <summary>The boost of the on-screen outline and shadow for <see cref="TextBackdropNeed.Stronger"/> (1 = the normal readable text).</summary>
    public const double StrongerBoost = 1.6;

    /// <summary>The boost for <see cref="TextBackdropNeed.Backdrop"/>: the engines add a soft halo behind the text from here on.</summary>
    public const double BackdropBoost = 2.2;

    private static readonly double[] LinearLevels = Enumerable.Range(0, 256).Select(i =>
    {
        var c = i / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }).ToArray();

    /// <summary>
    /// Measures <paramref name="pixels"/> against the text colour <paramref name="textHex"/>: mean luminance, contrast of the text with it,
    /// the share of pixels close to the text's brightness and how busy the region is.
    /// </summary>
    public static BackdropMeasure Measure(FramePixels pixels, string textHex)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var text = PaletteColors.TryParse(textHex, out var colour) ? colour : throw new ArgumentException($"'{textHex}' is not a colour.", nameof(textHex));
        if (pixels.Width < 1 || pixels.Height < 1 || pixels.Rgb.Length < pixels.Width * pixels.Height * 3)
        {
            throw new ArgumentException("The pixels do not match their size.", nameof(pixels));
        }

        var textLuminance = PaletteColors.Luminance(text);
        var rgb = pixels.Rgb.Span;
        var count = pixels.Width * pixels.Height;
        var luma = new double[count];
        double sum = 0;
        var close = 0;
        for (var i = 0; i < count; i++)
        {
            var r = rgb[i * 3];
            var g = rgb[(i * 3) + 1];
            var b = rgb[(i * 3) + 2];
            var luminance = (0.2126 * LinearLevels[r]) + (0.7152 * LinearLevels[g]) + (0.0722 * LinearLevels[b]);
            sum += luminance;
            if (Ratio(luminance, textLuminance) < LowContrast)
            {
                close++;
            }

            // Busyness is measured on the gamma-encoded brightness (as the eye sees steps), not on linear light.
            luma[i] = ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) / 255.0;
        }

        double steps = 0;
        var pairs = 0;
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                var at = (y * pixels.Width) + x;
                if (x + 1 < pixels.Width)
                {
                    steps += Math.Abs(luma[at] - luma[at + 1]);
                    pairs++;
                }

                if (y + 1 < pixels.Height)
                {
                    steps += Math.Abs(luma[at] - luma[at + pixels.Width]);
                    pairs++;
                }
            }
        }

        var mean = sum / count;
        return new BackdropMeasure(mean, Ratio(mean, textLuminance), (double)close / count, pairs == 0 ? 0 : steps / pairs);
    }

    /// <summary>
    /// The help text needs over <paramref name="measure"/>: a backdrop when much of it is close to the text's brightness (40 % or more) or it
    /// is very busy; a stronger outline when some of it is (10 %), the mean contrast is below WCAG's 4.5 or it is busy; else the normal one.
    /// </summary>
    public static TextBackdropNeed Need(BackdropMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        if (measure.LowContrastShare >= 0.4 || measure.Busyness >= 0.14)
        {
            return TextBackdropNeed.Backdrop;
        }

        return measure.LowContrastShare >= 0.1 || measure.Contrast < 4.5 || measure.Busyness >= 0.06 ? TextBackdropNeed.Stronger : TextBackdropNeed.Plain;
    }

    /// <summary>The on-screen outline and shadow boost of a need (1, <see cref="StrongerBoost"/>, <see cref="BackdropBoost"/>).</summary>
    public static double Boost(TextBackdropNeed need) => need switch
    {
        TextBackdropNeed.Backdrop => BackdropBoost,
        TextBackdropNeed.Stronger => StrongerBoost,
        _ => 1.0,
    };

    /// <summary>
    /// Where the subtitles are in a <paramref name="width"/> × <paramref name="height"/> video: between the side margins, two lines above the
    /// bottom margin (the place of <see cref="StyleOverlay"/>'s subtitles, bottom centre).
    /// </summary>
    public static FrameRegion SubtitleRegion(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 2);
        var size = SubtitleBuilder.FontSize(width, height);
        var side = SubtitleBuilder.SideMargin(width);
        var bottom = height - Math.Round(height * 0.12) + (size * 0.3);
        var top = Math.Max(0, bottom - (size * 2.9));
        return new FrameRegion((double)side / width, top / height, (double)(width - (2 * side)) / width, (Math.Min(height, bottom) - top) / height);
    }

    /// <summary>
    /// The subtitles sampled out of <paramref name="count"/>: every one up to <see cref="MaxSamples"/>, else that many spread evenly (first
    /// and last included).
    /// </summary>
    public static IReadOnlyList<int> SampleIndexes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count <= MaxSamples)
        {
            return Enumerable.Range(0, count).ToList();
        }

        return Enumerable.Range(0, MaxSamples).Select(i => (int)Math.Round(i * (count - 1) / (double)(MaxSamples - 1))).Distinct().ToList();
    }

    /// <summary>
    /// The need of every one of <paramref name="count"/> subtitles from the needs of the sampled ones (<paramref name="sampled"/>, by subtitle
    /// index): a subtitle that was not sampled takes the need of the nearest one that was; with none sampled, all are plain.
    /// </summary>
    public static IReadOnlyList<TextBackdropNeed> Spread(int count, IReadOnlyDictionary<int, TextBackdropNeed> sampled)
    {
        ArgumentNullException.ThrowIfNull(sampled);
        var known = sampled.Keys.Order().ToList();
        return Enumerable.Range(0, count)
            .Select(i => known.Count == 0 ? TextBackdropNeed.Plain : sampled[known.MinBy(k => Math.Abs(k - i))])
            .ToList();
    }

    private static double Ratio(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
}
