namespace TMConcept.Core.References;

/// <summary>
/// The main colours of a set of CIELAB samples: k-means in CIELAB (distance = ΔE76), started from the samples at evenly spaced lightness
/// quantiles so the result is the same on every run, <see cref="ReferenceLimits.PaletteRounds"/> rounds; the clusters by size, most of the
/// picture first, each as #RRGGBB. Empty clusters and one that is nearly the same colour as a bigger one (ΔE below 6) are left out. Pure.
/// </summary>
public static class PaletteClusters
{
    private const double SameColour = 6;

    /// <summary>At most <paramref name="count"/> main colours of <paramref name="samples"/> (none for no samples).</summary>
    public static IReadOnlyList<string> Main(IReadOnlyList<(double L, double A, double B)> samples, int count)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0 || count <= 0)
        {
            return [];
        }

        var k = Math.Min(count, samples.Count);
        var sorted = samples.OrderBy(s => s.L).ThenBy(s => s.A).ThenBy(s => s.B).ToList();
        var centres = Enumerable.Range(0, k).Select(i => sorted[(int)((i + 0.5) * sorted.Count / k)]).ToArray();
        var owner = new int[samples.Count];
        for (var round = 0; round < ReferenceLimits.PaletteRounds; round++)
        {
            var changed = false;
            for (var i = 0; i < samples.Count; i++)
            {
                var best = Nearest(centres, samples[i]);
                changed |= best != owner[i];
                owner[i] = best;
            }

            var sums = new (double L, double A, double B, int N)[k];
            for (var i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                var o = owner[i];
                sums[o] = (sums[o].L + s.L, sums[o].A + s.A, sums[o].B + s.B, sums[o].N + 1);
            }

            for (var c = 0; c < k; c++)
            {
                if (sums[c].N > 0)
                {
                    centres[c] = (sums[c].L / sums[c].N, sums[c].A / sums[c].N, sums[c].B / sums[c].N);
                }
            }

            if (!changed && round > 0)
            {
                break;
            }
        }

        var sizes = new int[k];
        foreach (var o in owner)
        {
            sizes[o]++;
        }

        var kept = new List<(double L, double A, double B)>();
        foreach (var c in Enumerable.Range(0, k).Where(c => sizes[c] > 0).OrderByDescending(c => sizes[c]).ThenBy(c => c))
        {
            if (kept.All(o => Distance(o, centres[c]) >= SameColour))
            {
                kept.Add(centres[c]);
            }
        }

        return [.. kept.Select(c => LabColor.ToHex(c.L, c.A, c.B))];
    }

    private static int Nearest((double L, double A, double B)[] centres, (double L, double A, double B) sample)
    {
        var best = 0;
        var distance = double.MaxValue;
        for (var c = 0; c < centres.Length; c++)
        {
            var d = Distance(centres[c], sample);
            if (d < distance)
            {
                distance = d;
                best = c;
            }
        }

        return best;
    }

    private static double Distance((double L, double A, double B) first, (double L, double A, double B) second)
    {
        var (dl, da, db) = (first.L - second.L, first.A - second.A, first.B - second.B);
        return Math.Sqrt((dl * dl) + (da * da) + (db * db));
    }
}
