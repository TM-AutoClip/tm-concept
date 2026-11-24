namespace TMConcept.Core.Music;

/// <summary>
/// The beat grid of a track (F7, 0.1.45; ReelMimic's <c>beat_grid.py</c>, MIT, written again in C#, no Python): the onset envelope
/// (<see cref="SpectrumFlux"/>), the tempo between <see cref="MusicLibraryRules.MinBpm"/> and <see cref="MusicLibraryRules.MaxBpm"/> where
/// the envelope's autocorrelation (between two lags, interpolated) is highest, then the phase whose comb of beats collects the most onset
/// energy. The envelope's step <c>i</c> compares frame <c>i + 1</c> with frame <c>i</c>; a sound has risen most when it reaches the middle of
/// that frame's window, so a step is placed there (<c>beat_grid.py</c> prints the frame's start, about 50 ms early; measured on click tracks). Pure.
/// </summary>
public static class BeatTracker
{
    /// <summary>The grid of mono <paramref name="samples"/> at <paramref name="sampleRate"/>; <c>null</c> when they are too short or silent.</summary>
    public static BeatGrid? Analyze(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        var flux = SpectrumFlux.Envelope(samples);
        return Grid(flux, sampleRate);
    }

    /// <summary>The grid of an onset envelope (<see cref="SpectrumFlux.Envelope"/>) measured at <paramref name="sampleRate"/>; <c>null</c> without onsets.</summary>
    public static BeatGrid? Grid(IReadOnlyList<double> flux, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(flux);
        var fps = sampleRate / (double)MusicLibraryRules.FluxHop;
        var longest = (int)Math.Ceiling(fps * 60 / MusicLibraryRules.MinBpm) + 2;
        if (flux.Count <= longest * 2 || flux.All(v => v <= 0))
        {
            return null;
        }

        // Only the lags of the searched tempos are needed (not the whole autocorrelation).
        var ac = new double[longest + 1];
        for (var lag = 0; lag <= longest; lag++)
        {
            var sum = 0.0;
            for (var t = 0; t + lag < flux.Count; t++)
            {
                sum += flux[t] * flux[t + lag];
            }

            ac[lag] = sum;
        }

        var bpm = MusicLibraryRules.MinBpm;
        var best = double.MinValue;
        var steps = (int)Math.Round((MusicLibraryRules.MaxBpm - MusicLibraryRules.MinBpm) / MusicLibraryRules.BpmStep);
        for (var s = 0; s < steps; s++)
        {
            var candidate = MusicLibraryRules.MinBpm + (s * MusicLibraryRules.BpmStep);
            var lag = fps * 60 / candidate;
            var whole = (int)lag;
            var part = lag - whole;
            var score = (ac[whole] * (1 - part)) + (ac[whole + 1] * part);
            if (score > best)
            {
                best = score;
                bpm = candidate;
            }
        }

        // The autocorrelation's peak is a whole lag (one lag is 5.8 ms, so 165 bpm reads as 164.1): the tempo is refined around it by the comb
        // itself (an addition to beat_grid.py), so the grid does not drift away from the beats over a long track.
        var (phase, refined) = (0.0, bpm);
        var collected = double.MinValue;
        var low = bpm * (1 - MusicLibraryRules.RefineShare);
        var count = (int)Math.Round(bpm * 2 * MusicLibraryRules.RefineShare / MusicLibraryRules.RefineStep);
        for (var s = 0; s <= count; s++)
        {
            var candidate = low + (s * MusicLibraryRules.RefineStep);
            var (at, score) = Comb(flux, fps, 60 / candidate);
            if (score > collected + 1e-12)
            {
                (collected, phase, refined) = (score, at, candidate);
            }
        }

        var beat = 60 / refined;
        // Step i compares frame i + 1 with frame i: a sound has risen most once it reaches the middle of frame i + 1's Hann window.
        var lead = MusicLibraryRules.OnsetLeadSamples / (double)sampleRate;
        var first = (phase + lead) % beat;
        return new BeatGrid(Math.Round(refined, 2), Math.Round(first, 4));
    }

    /// <summary>
    /// The phase whose comb of beats every <paramref name="beat"/> seconds collects the most onset energy (the envelope read between frames),
    /// and that energy. A sound's rise spreads over the frames its window takes to reach it, so the sums are smoothed over a few phases around
    /// the circle and the middle of the spread wins.
    /// </summary>
    private static (double Phase, double Score) Comb(IReadOnlyList<double> flux, double fps, double beat)
    {
        var length = flux.Count / fps;
        var phases = (int)Math.Ceiling(beat / MusicLibraryRules.PhaseStep);
        var sums = new double[phases];
        for (var j = 0; j < phases; j++)
        {
            var sum = 0.0;
            for (var t = j * MusicLibraryRules.PhaseStep; t < length; t += beat)
            {
                var x = t * fps;
                var index = (int)x;
                if (index + 1 < flux.Count)
                {
                    var part = x - index;
                    sum += (flux[index] * (1 - part)) + (flux[index + 1] * part);
                }
            }

            sums[j] = sum;
        }

        var phase = 0.0;
        var best = double.MinValue;
        var half = Math.Max(1, (int)Math.Round(MusicLibraryRules.PhaseSmoothSeconds / MusicLibraryRules.PhaseStep));
        for (var j = 0; j < phases; j++)
        {
            var sum = 0.0;
            for (var k = -half; k <= half; k++)
            {
                sum += sums[(((j + k) % phases) + phases) % phases];
            }

            if (sum > best + 1e-12)
            {
                best = sum;
                phase = j * MusicLibraryRules.PhaseStep;
            }
        }

        return (phase, best);
    }
}
