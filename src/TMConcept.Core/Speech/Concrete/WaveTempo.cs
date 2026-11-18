namespace TMConcept.Core.Speech;

/// <summary>
/// Changes the speed of speech without changing its pitch (WSOLA: overlapping windows of the input, each placed where it continues the
/// previous one best, added at a fixed step of the output). For the voices that have no speed of their own (<see cref="ISpeechEngine.HandlesSpeed"/>):
/// before 0.1.24 the speed chosen for them was silently dropped. Mono samples in [-1, 1]; a speed of 1 returns the samples unchanged.
/// </summary>
public static class WaveTempo
{
    /// <summary>A speed closer to 1 than this is left as it is.</summary>
    public const double Tolerance = 0.005;

    /// <summary>
    /// Speaks <paramref name="wav"/> (a mono 16-bit PCM file) at <paramref name="speed"/> times its speed, in place; returns its new length in
    /// milliseconds. <see cref="InvalidDataException"/> when the file is not mono 16-bit PCM.
    /// </summary>
    public static long Apply(string wav, double speed)
    {
        var (samples, rate) = WaveFile.ReadMonoPcm16(wav);
        if (Math.Abs(speed - 1) < Tolerance || samples.Length == 0)
        {
            return WaveFile.DurationMs(wav);
        }

        var stretched = Stretch(samples, rate, speed);
        WaveFile.WritePcm16(wav, stretched, rate);
        return (long)Math.Round(stretched.Length * 1000.0 / rate);
    }

    /// <summary>The samples at <paramref name="speed"/> times their speed (about <c>length / speed</c> samples), at the same pitch.</summary>
    public static float[] Stretch(ReadOnlySpan<float> input, int sampleRate, double speed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(speed);
        if (Math.Abs(speed - 1) < Tolerance || input.Length == 0)
        {
            return input.ToArray();
        }

        // 40 ms windows, half of each overlaps the next; the best join is looked for within 10 ms of the nominal place.
        var frame = Math.Max(64, sampleRate / 25);
        var step = frame / 2;
        var search = Math.Max(8, sampleRate / 100);
        var outputLength = (int)Math.Round(input.Length / speed);
        var output = new double[outputLength + frame];
        var weight = new double[outputLength + frame];
        var window = new double[frame];
        for (var i = 0; i < frame; i++)
        {
            window[i] = 0.5 - (0.5 * Math.Cos(2 * Math.PI * i / frame));
        }

        var previous = 0;
        for (var k = 0; k * step < outputLength; k++)
        {
            var at = k * step;
            var nominal = (int)Math.Round(k * step * speed);
            var source = k == 0 ? 0 : Best(input, previous + step, nominal, search, step);
            for (var i = 0; i < frame; i++)
            {
                var j = source + i;
                var sample = j >= 0 && j < input.Length ? input[j] : 0f;
                output[at + i] += sample * window[i];
                weight[at + i] += window[i];
            }

            previous = source;
        }

        var result = new float[outputLength];
        for (var i = 0; i < outputLength; i++)
        {
            result[i] = (float)Math.Clamp(weight[i] > 1e-3 ? output[i] / weight[i] : output[i], -1, 1);
        }

        return result;
    }

    /// <summary>
    /// The input position within <paramref name="search"/> of <paramref name="nominal"/> whose next <paramref name="length"/> samples are most
    /// like those at <paramref name="natural"/> (where the previous window would have continued).
    /// </summary>
    private static int Best(ReadOnlySpan<float> input, int natural, int nominal, int search, int length)
    {
        var best = nominal;
        var bestScore = double.NegativeInfinity;
        for (var candidate = Math.Max(0, nominal - search); candidate <= nominal + search; candidate++)
        {
            double score = 0;
            for (var i = 0; i < length; i++)
            {
                var a = natural + i;
                var b = candidate + i;
                if (a >= input.Length || b >= input.Length)
                {
                    break;
                }

                score += input[a] * input[b];
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }
}
