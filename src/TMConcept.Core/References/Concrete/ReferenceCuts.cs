using TMConcept.Core.Videos;

namespace TMConcept.Core.References;

/// <summary>
/// The cuts of a reference (S1, 0.1.52): a frame is a cut when most of the grid changed a lot at once and the whole frame changed (the motion
/// check's thresholds, K07: <see cref="MotionCheckLimits.CutShare"/>, <see cref="MotionCheckLimits.CutChange"/>) and its grey histogram is
/// clearly another one (<see cref="ReferenceLimits.CutCorrelation"/>; a fast pan changes the pixels, not the histogram). Cuts closer than
/// <see cref="ReferenceLimits.CutGapSeconds"/> are one. Pure.
/// </summary>
public static class ReferenceCuts
{
    /// <summary>The frame index of every cut, in order (never frame 0).</summary>
    public static IReadOnlyList<int> Find(ReferenceFrameSeries frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var cuts = new List<int>();
        var gap = Math.Max(1, (int)Math.Round(ReferenceLimits.CutGapSeconds * frames.Fps));
        for (var i = 1; i < frames.Count; i++)
        {
            if (frames.CutShare[i] > MotionCheckLimits.CutShare && frames.Change[i] > MotionCheckLimits.CutChange
                && frames.Correlation[i] < ReferenceLimits.CutCorrelation && (cuts.Count == 0 || i - cuts[^1] >= gap))
            {
                cuts.Add(i);
            }
        }

        return cuts;
    }

    /// <summary>The shots between the cuts: (first frame, frames), from frame 0 to the end.</summary>
    public static IReadOnlyList<(int Start, int Length)> Shots(IReadOnlyList<int> cuts, int frames)
    {
        ArgumentNullException.ThrowIfNull(cuts);
        var shots = new List<(int, int)>();
        var start = 0;
        foreach (var cut in cuts.Where(c => c > 0 && c < frames))
        {
            shots.Add((start, cut - start));
            start = cut;
        }

        if (frames > start)
        {
            shots.Add((start, frames - start));
        }

        return shots;
    }
}
