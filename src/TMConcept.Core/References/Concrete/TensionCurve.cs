namespace TMConcept.Core.References;

/// <summary>
/// The tension of a reference and its peaks (S1, 0.1.52; ReelMimic video-clone <c>perception/tension.py</c>, MIT, written again in C# with
/// the features this app measures): on the frame clock, each feature has its dead zone taken off, is mapped from its 5th–95th percentile to
/// 0–1 without stretching a range smaller than a viewer notices (JND), weighted (loudness 0.28, camera 0.14, frame difference 0.10, cut density
/// 0.10; a feature that was not measured, the loudness of a silent video, leaves the mix), summed and smoothed (Gaussian, 0.15 s). The peaks
/// are its local tops with a prominence of at least 0.12, at least 4 s apart (the higher wins), the three strongest by prominence × height,
/// each at the start of its plateau (97 %), in time order. Pure.
/// </summary>
public static class TensionCurve
{
    /// <summary>
    /// The tension of <paramref name="count"/> frames at <paramref name="fps"/> from the loudness (dB per frame, or null), the camera speed, the
    /// frame difference and the cut frames.
    /// </summary>
    public static double[] Compute(int count, double fps, IReadOnlyList<double>? loudness, IReadOnlyList<double> camera, IReadOnlyList<double> change,
        IReadOnlyList<int> cuts)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(cuts);
        if (count <= 0)
        {
            return [];
        }

        var impulses = new double[count];
        foreach (var cut in cuts.Where(c => c >= 0 && c < count))
        {
            impulses[cut] = 1;
        }

        var density = Smooth(impulses, ReferenceLimits.CutSpreadSeconds * fps).Select(v => v * fps).ToArray();
        var parts = new List<(double Weight, double[] Values)>
        {
            (ReferenceLimits.CameraWeight, Robust01(Fit(camera, count).Select(v => Math.Max(0, v - ReferenceLimits.CameraDeadZone)).ToArray(), ReferenceLimits.CameraJnd)),
            (ReferenceLimits.ChangeWeight, Robust01(Fit(change, count).Select(v => Math.Max(0, v - ReferenceLimits.ChangeDeadZone)).ToArray(), ReferenceLimits.ChangeJnd)),
            (ReferenceLimits.CutWeight, Robust01(density, ReferenceLimits.CutJnd)),
        };
        if (loudness is { Count: > 0 })
        {
            parts.Add((ReferenceLimits.LoudnessWeight, Robust01(Fit(loudness, count), ReferenceLimits.LoudnessJnd)));
        }

        var total = parts.Sum(p => p.Weight);
        var mix = new double[count];
        for (var i = 0; i < count; i++)
        {
            mix[i] = parts.Sum(p => p.Weight * p.Values[i]) / total;
        }

        return Smooth(mix, ReferenceLimits.SmoothSeconds * fps);
    }

    /// <summary>The peaks of <paramref name="tension"/> (seconds, in order; at most <see cref="ReferenceLimits.Peaks"/>).</summary>
    public static IReadOnlyList<double> Peaks(IReadOnlyList<double> tension, double fps)
    {
        ArgumentNullException.ThrowIfNull(tension);
        var n = tension.Count;
        if (n < Math.Max(3, (int)fps))
        {
            return [];
        }

        // Local tops (a plateau counts once, at its middle like scipy's find_peaks).
        var tops = new List<int>();
        for (var i = 1; i < n - 1; i++)
        {
            if (tension[i] <= tension[i - 1])
            {
                continue;
            }

            var j = i;
            while (j + 1 < n && Math.Abs(tension[j + 1] - tension[i]) < 1e-12)
            {
                j++;
            }

            if (j + 1 < n && tension[j + 1] < tension[i])
            {
                tops.Add((i + j) / 2);
            }

            i = j;
        }

        // Distance: the higher top keeps its place, a lower one within the gap is dropped.
        var gap = ReferenceLimits.PeakGapSeconds * fps;
        var kept = new List<int>();
        foreach (var top in tops.OrderByDescending(t => tension[t]).ThenBy(t => t))
        {
            if (kept.All(k => Math.Abs(k - top) >= gap))
            {
                kept.Add(top);
            }
        }

        var scored = kept.Select(k => (Index: k, Prominence: Prominence(tension, k))).Where(p => p.Prominence >= ReferenceLimits.PeakProminence)
            .OrderByDescending(p => p.Prominence * tension[p.Index]).ThenBy(p => p.Index).Take(ReferenceLimits.Peaks).ToList();
        return [.. scored.Select(p => Hit(tension, p.Index) / fps).Order()];
    }

    /// <summary>The 5th–95th percentile of <paramref name="values"/> mapped to 0–1, never stretching a range smaller than <paramref name="jnd"/>.</summary>
    public static double[] Robust01(IReadOnlyList<double> values, double jnd)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return [];
        }

        var sorted = values.Order().ToArray();
        var low = Percentile(sorted, 0.05);
        var high = Percentile(sorted, 0.95);
        var span = Math.Max(high - low, jnd);
        return span <= 1e-9 ? new double[values.Count] : [.. values.Select(v => Math.Clamp((v - low) / span, 0, 1))];
    }

    /// <summary>A Gaussian smoothing with <paramref name="sigma"/> samples (the ends repeat their last value).</summary>
    public static double[] Smooth(IReadOnlyList<double> values, double sigma)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (sigma <= 0 || values.Count == 0)
        {
            return [.. values];
        }

        var radius = (int)Math.Ceiling(sigma * 4);
        var kernel = Enumerable.Range(-radius, (2 * radius) + 1).Select(k => Math.Exp(-(k * k) / (2 * sigma * sigma))).ToArray();
        var sum = kernel.Sum();
        var result = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var value = 0.0;
            for (var k = -radius; k <= radius; k++)
            {
                value += kernel[k + radius] * values[Math.Clamp(i + k, 0, values.Count - 1)];
            }

            result[i] = value / sum;
        }

        return result;
    }

    private static double Percentile(double[] sorted, double share)
    {
        var position = share * (sorted.Length - 1);
        var below = (int)Math.Floor(position);
        var above = Math.Min(below + 1, sorted.Length - 1);
        return sorted[below] + ((sorted[above] - sorted[below]) * (position - below));
    }

    /// <summary>The prominence of a top (scipy's): its height above the higher of the lowest points on each side up to a higher point.</summary>
    private static double Prominence(IReadOnlyList<double> t, int k)
    {
        var leftMin = t[k];
        for (var i = k - 1; i >= 0 && t[i] <= t[k]; i--)
        {
            leftMin = Math.Min(leftMin, t[i]);
        }

        var rightMin = t[k];
        for (var i = k + 1; i < t.Count && t[i] <= t[k]; i++)
        {
            rightMin = Math.Min(rightMin, t[i]);
        }

        return t[k] - Math.Max(leftMin, rightMin);
    }

    /// <summary>The hit: where the top's plateau (at least 97 % of it) starts.</summary>
    private static int Hit(IReadOnlyList<double> t, int k)
    {
        var f = k;
        while (f > 0 && t[f - 1] >= 0.97 * t[k])
        {
            f--;
        }

        return f;
    }

    private static double[] Fit(IReadOnlyList<double> values, int count)
    {
        var result = new double[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = values.Count == 0 ? 0 : values[Math.Min(i, values.Count - 1)];
        }

        return result;
    }
}
