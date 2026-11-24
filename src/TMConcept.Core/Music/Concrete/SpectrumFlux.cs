namespace TMConcept.Core.Music;

/// <summary>
/// The onset envelope of a track (F7, 0.1.45; the method of ReelMimic's <c>beat_grid.py</c>, MIT, written again in C#): frames of
/// <see cref="MusicLibraryRules.FluxWindow"/> samples every <see cref="MusicLibraryRules.FluxHop"/> samples under a Hann window, the log
/// magnitude of each frame's spectrum (<c>log(1 + |X|)</c>), the rise from one frame to the next summed over the bins (spectral flux), then
/// the local mean of <see cref="MusicLibraryRules.FluxMean"/> frames taken off and what is left below zero cut. Pure: samples in, numbers out.
/// </summary>
public static class SpectrumFlux
{
    /// <summary>The onset strength of every frame step (frame <c>i + 1</c> against frame <c>i</c>); empty when the samples are shorter than a frame.</summary>
    public static double[] Envelope(ReadOnlySpan<float> samples)
    {
        const int window = MusicLibraryRules.FluxWindow;
        const int hop = MusicLibraryRules.FluxHop;
        if (samples.Length < window)
        {
            return [];
        }

        var frames = ((samples.Length - window) / hop) + 1;
        var hann = new double[window];
        for (var i = 0; i < window; i++)
        {
            hann[i] = 0.5 - (0.5 * Math.Cos(2 * Math.PI * i / (window - 1)));
        }

        var (cos, sin) = Twiddles(window);
        var bins = (window / 2) + 1;
        var previous = new double[bins];
        var current = new double[bins];
        var re = new double[window];
        var im = new double[window];
        var flux = new double[frames - 1];
        for (var f = 0; f < frames; f++)
        {
            var start = f * hop;
            for (var i = 0; i < window; i++)
            {
                re[i] = samples[start + i] * hann[i];
                im[i] = 0;
            }

            Transform(re, im, cos, sin);
            for (var k = 0; k < bins; k++)
            {
                current[k] = Math.Log(1 + Math.Sqrt((re[k] * re[k]) + (im[k] * im[k])));
            }

            if (f > 0)
            {
                var sum = 0.0;
                for (var k = 0; k < bins; k++)
                {
                    var rise = current[k] - previous[k];
                    if (rise > 0)
                    {
                        sum += rise;
                    }
                }

                flux[f - 1] = sum;
            }

            (previous, current) = (current, previous);
        }

        return LessLocalMean(flux);
    }

    /// <summary>
    /// <paramref name="flux"/> less its centred moving mean of <see cref="MusicLibraryRules.FluxMean"/> frames (zero outside the envelope, as
    /// numpy's <c>convolve(..., 'same')</c>), negative values cut to zero.
    /// </summary>
    public static double[] LessLocalMean(IReadOnlyList<double> flux)
    {
        ArgumentNullException.ThrowIfNull(flux);
        const int width = MusicLibraryRules.FluxMean;
        var n = flux.Count;
        var prefix = new double[n + 1];
        for (var i = 0; i < n; i++)
        {
            prefix[i + 1] = prefix[i] + flux[i];
        }

        // numpy's 'same' of an even kernel of 64: the value at i is the mean of flux[i - 32 .. i + 31].
        var before = width / 2;
        var result = new double[n];
        for (var i = 0; i < n; i++)
        {
            var from = Math.Max(0, i - before);
            var to = Math.Min(n, i - before + width);
            var mean = (prefix[to] - prefix[from]) / width;
            result[i] = Math.Max(0, flux[i] - mean);
        }

        return result;
    }

    private static (double[] Cos, double[] Sin) Twiddles(int n)
    {
        var cos = new double[n / 2];
        var sin = new double[n / 2];
        for (var i = 0; i < n / 2; i++)
        {
            cos[i] = Math.Cos(-2 * Math.PI * i / n);
            sin[i] = Math.Sin(-2 * Math.PI * i / n);
        }

        return (cos, sin);
    }

    /// <summary>An in-place radix-2 FFT (the length is a power of two).</summary>
    private static void Transform(double[] re, double[] im, double[] cos, double[] sin)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var size = 2; size <= n; size <<= 1)
        {
            var half = size >> 1;
            var step = n / size;
            for (var start = 0; start < n; start += size)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = cos[k * step];
                    var wi = sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var tr = (re[b] * wr) - (im[b] * wi);
                    var ti = (re[b] * wi) + (im[b] * wr);
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }
}
