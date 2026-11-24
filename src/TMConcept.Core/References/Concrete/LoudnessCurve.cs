namespace TMConcept.Core.References;

/// <summary>The loudness of a sound: the RMS in dBFS of every window of <c>step</c> seconds (ReelMimic <c>tension.py</c> uses 20·log10(RMS)). Pure.</summary>
public static class LoudnessCurve
{
    private const double Floor = 1e-6;

    /// <summary>One value per <paramref name="stepSeconds"/> of <paramref name="samples"/> at <paramref name="sampleRate"/> (a last partial window counts).</summary>
    public static double[] Rms(ReadOnlySpan<float> samples, int sampleRate, double stepSeconds)
    {
        if (sampleRate <= 0 || stepSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        var window = Math.Max(1, (int)Math.Round(sampleRate * stepSeconds));
        var count = (samples.Length + window - 1) / window;
        var result = new double[count];
        for (var w = 0; w < count; w++)
        {
            var start = w * window;
            var end = Math.Min(samples.Length, start + window);
            var sum = 0.0;
            for (var i = start; i < end; i++)
            {
                sum += samples[i] * (double)samples[i];
            }

            result[w] = 20 * Math.Log10(Math.Sqrt(sum / Math.Max(1, end - start)) + Floor);
        }

        return result;
    }
}
