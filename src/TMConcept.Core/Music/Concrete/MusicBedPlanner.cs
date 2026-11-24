namespace TMConcept.Core.Music;

/// <summary>
/// Where a track starts under a video and how it is made long enough (F8, 0.1.45; the idea of ReelMimic's <c>analyze.py</c>, MIT, and of its
/// background music notes, Apache-2.0, numbers and ideas only). Pure:
/// <list type="bullet">
/// <item><see cref="Measure"/>: the loudness (RMS) of every half second.</item>
/// <item><see cref="Entry"/>: the strongest clean opening on a bar line: its first <see cref="MusicLibraryRules.EntryPartSeconds"/> are compared
/// with the parts after it; an opening quieter than <see cref="MusicLibraryRules.EntryCleanShare"/> of them (a soft intro) is not clean.</item>
/// <item><see cref="TailEnd"/>: where the track's quiet end starts (no silent tail is looped or left at the video's end).</item>
/// <item><see cref="Plan"/>: the part from the entry played once, or, when it is shorter than the video, repeated from bar line to bar line
/// with a cross-fade of one beat between copies.</item>
/// </list>
/// </summary>
public static class MusicBedPlanner
{
    /// <summary>The loudness of <paramref name="samples"/> per <see cref="MusicLibraryRules.EnergyWindowSeconds"/>.</summary>
    public static MusicEnergy Measure(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        var size = Math.Max(1, (int)Math.Round(sampleRate * MusicLibraryRules.EnergyWindowSeconds));
        var rms = new List<double>();
        for (var start = 0; start + size <= samples.Length; start += size)
        {
            var sum = 0.0;
            foreach (var sample in samples.Slice(start, size))
            {
                sum += sample * (double)sample;
            }

            rms.Add(Math.Sqrt(sum / size));
        }

        return new MusicEnergy(rms, size / (double)sampleRate);
    }

    /// <summary>The end of the track's last window that is not silence (its quiet tail starts there); the whole length when it has none.</summary>
    public static double TailEnd(MusicEnergy energy)
    {
        ArgumentNullException.ThrowIfNull(energy);
        if (energy.Rms.Count == 0)
        {
            return 0;
        }

        var floor = Median(energy.Rms) * MusicLibraryRules.TailSilenceShare;
        for (var i = energy.Rms.Count - 1; i >= 0; i--)
        {
            if (energy.Rms[i] > floor)
            {
                return (i + 1) * energy.WindowSeconds;
            }
        }

        return energy.Seconds;
    }

    /// <summary>
    /// The entry for a video part of <paramref name="needSeconds"/>: a bar line of <paramref name="grid"/> (every window start without one),
    /// late enough that the rest still fits when the track is long enough, otherwise early enough to leave a loop of
    /// <see cref="MusicLibraryRules.MinLoopSeconds"/>. Clean openings come first, then the loudest; ties take the earliest.
    /// </summary>
    public static double Entry(MusicEnergy energy, BeatGrid? grid, double needSeconds)
    {
        ArgumentNullException.ThrowIfNull(energy);
        var tail = TailEnd(energy);
        var latest = tail - needSeconds >= 0 ? tail - needSeconds : Math.Min(tail - MusicLibraryRules.MinLoopSeconds, tail / 2);
        var step = grid?.BarSeconds ?? MusicLibraryRules.EntryPartSeconds;
        var first = grid?.FirstBeatSeconds ?? 0;
        if (latest < first)
        {
            return first;
        }

        var best = first;
        var bestClean = false;
        var bestLoud = double.MinValue;
        for (var t = first; t <= latest + 1e-9; t += step)
        {
            var open = Mean(energy, t, t + MusicLibraryRules.EntryPartSeconds);
            var after = new List<double>();
            for (var part = 1; part <= MusicLibraryRules.EntryParts; part++)
            {
                var from = t + (part * MusicLibraryRules.EntryPartSeconds);
                if (from + MusicLibraryRules.EntryPartSeconds <= tail)
                {
                    after.Add(Mean(energy, from, from + MusicLibraryRules.EntryPartSeconds));
                }
            }

            var following = after.Count > 0 ? after.Average() : open;
            var clean = following <= 0 || open >= following * MusicLibraryRules.EntryCleanShare;
            if ((clean && !bestClean) || (clean == bestClean && open > bestLoud + 1e-12))
            {
                best = t;
                bestClean = clean;
                bestLoud = open;
            }
        }

        return Math.Round(best, 3);
    }

    /// <summary>
    /// The bed for <paramref name="needSeconds"/> of video from <paramref name="entrySeconds"/> of a track whose sound ends at
    /// <paramref name="tailSeconds"/>: played once when it is long enough; otherwise the part up to the last bar line before the tail (the
    /// tail itself without a grid) is repeated with a cross-fade of one beat (<see cref="MusicLibraryRules.MinCrossfadeSeconds"/> to
    /// <see cref="MusicLibraryRules.MaxCrossfadeSeconds"/>, at most a quarter of the part), at most <see cref="MusicLibraryRules.MaxCopies"/>
    /// times.
    /// </summary>
    public static MusicBed Plan(double entrySeconds, double needSeconds, double tailSeconds, BeatGrid? grid)
    {
        if (needSeconds <= 0 || tailSeconds <= entrySeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(needSeconds), "The track must have sound after its entry and a length must be needed.");
        }

        var available = tailSeconds - entrySeconds;
        if (available >= needSeconds)
        {
            return new MusicBed(entrySeconds, Math.Round(entrySeconds + needSeconds, 3), 1, 0, needSeconds);
        }

        var length = available;
        if (grid is not null)
        {
            var bars = Math.Floor(available / grid.BarSeconds);
            if (bars * grid.BarSeconds >= MusicLibraryRules.MinLoopSeconds)
            {
                length = bars * grid.BarSeconds;
            }
        }

        var beat = grid?.BeatSeconds ?? 1;
        var fade = Math.Min(Math.Clamp(beat, MusicLibraryRules.MinCrossfadeSeconds, MusicLibraryRules.MaxCrossfadeSeconds), length / 4);
        var copies = (int)Math.Ceiling((needSeconds - fade) / (length - fade));
        copies = Math.Clamp(copies, 2, MusicLibraryRules.MaxCopies);
        return new MusicBed(entrySeconds, Math.Round(entrySeconds + length, 3), copies, Math.Round(fade, 3), needSeconds);
    }

    /// <summary>The mean loudness at <paramref name="seconds"/> (the window it falls in; 0 outside the track).</summary>
    public static double At(MusicEnergy energy, double seconds)
    {
        ArgumentNullException.ThrowIfNull(energy);
        var index = (int)Math.Floor(seconds / energy.WindowSeconds);
        return index >= 0 && index < energy.Rms.Count ? energy.Rms[index] : 0;
    }

    /// <summary>The median loudness of a track.</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
    }

    private static double Mean(MusicEnergy energy, double from, double to)
    {
        var first = Math.Max(0, (int)Math.Floor(from / energy.WindowSeconds));
        var last = Math.Min(energy.Rms.Count, (int)Math.Ceiling(to / energy.WindowSeconds));
        return last > first ? energy.Rms.Skip(first).Take(last - first).Average() : 0;
    }
}
