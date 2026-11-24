using TMConcept.Core.Videos;

namespace TMConcept.Core.References;

/// <summary>
/// The camera of a reference (S1, 0.1.52) from its frame shifts (<see cref="ReferenceFrameReader"/>): a shot of at least
/// <see cref="ReferenceLimits.ShortShotSeconds"/> has a camera move when the camera moved on at least <see cref="ReferenceLimits.CameraShotShare"/>
/// of its frames (the cut frame itself is not counted); the main way is the axis and sign of the summed moves when they agree enough (a pan
/// that goes back and forth names none); the ease shape is the vote of every move of at least 0.6 s: faster in its first third than its last
/// by <see cref="ReferenceLimits.EaseRatio"/> = out, slower = in, otherwise in-out (ReelMimic <c>curves.py</c> fits camera moves to ease
/// families, MIT; this is the coarse version). Pure.
/// </summary>
public static class ReferenceCameraMeasure
{
    private const double Agreement = 0.25;
    private const double MoveSeconds = 0.6;

    /// <summary>The camera of <paramref name="frames"/> with the cuts <paramref name="cuts"/>.</summary>
    public static ReferenceCamera Measure(ReferenceFrameSeries frames, IReadOnlyList<int> cuts)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(cuts);
        var cutSet = cuts.ToHashSet();
        var judged = 0;
        var moving = 0;
        foreach (var (start, length) in ReferenceCuts.Shots(cuts, frames.Count))
        {
            if (length / frames.Fps < ReferenceLimits.ShortShotSeconds)
            {
                continue;
            }

            judged++;
            var moved = Enumerable.Range(start + 1, length - 1).Count(i => frames.Moved[i]);
            if (moved >= ReferenceLimits.CameraShotShare * (length - 1))
            {
                moving++;
            }
        }

        double sumX = 0, sumY = 0, path = 0;
        for (var i = 1; i < frames.Count; i++)
        {
            if (frames.Moved[i] && !cutSet.Contains(i))
            {
                sumX += frames.ShiftX[i];
                sumY += frames.ShiftY[i];
                path += Math.Abs(frames.ShiftX[i]) + Math.Abs(frames.ShiftY[i]);
            }
        }

        var direction = CameraDirection.None;
        if (path > 0)
        {
            var horizontal = Math.Abs(sumX) >= Math.Abs(sumY);
            var along = horizontal ? sumX : sumY;
            if (Math.Abs(along) >= Agreement * path)
            {
                direction = horizontal ? (along > 0 ? CameraDirection.Right : CameraDirection.Left) : (along > 0 ? CameraDirection.Down : CameraDirection.Up);
            }
        }

        return new ReferenceCamera(judged == 0 ? 0 : moving / (double)judged, direction, Ease(frames, cutSet));
    }

    private static EaseShape Ease(ReferenceFrameSeries frames, HashSet<int> cuts)
    {
        var shortest = Math.Max(3, (int)Math.Round(MoveSeconds * frames.Fps));
        var votes = new Dictionary<EaseShape, int>();
        var i = 1;
        while (i < frames.Count)
        {
            if (!frames.Moved[i] || cuts.Contains(i))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < frames.Count && frames.Moved[i] && (i == start || !cuts.Contains(i)))
            {
                i++;
            }

            var length = i - start;
            if (length < shortest)
            {
                continue;
            }

            var third = length / 3;
            double Speed(int from, int count) => Enumerable.Range(from, count)
                .Average(k => Math.Sqrt((frames.ShiftX[k] * frames.ShiftX[k]) + (frames.ShiftY[k] * frames.ShiftY[k])));
            var first = Speed(start, third);
            var last = Speed(start + length - third, third);
            var shape = first > last * ReferenceLimits.EaseRatio ? EaseShape.Out : last > first * ReferenceLimits.EaseRatio ? EaseShape.In : EaseShape.InOut;
            votes[shape] = votes.GetValueOrDefault(shape) + 1;
        }

        if (votes.Count == 0)
        {
            return EaseShape.InOut;
        }

        var top = votes.Values.Max();
        var leaders = votes.Where(v => v.Value == top).Select(v => v.Key).ToList();
        return leaders.Count == 1 ? leaders[0] : EaseShape.InOut;
    }

    /// <summary>The camera speed of every frame (frame widths a second; 0 where it did not move or at a cut).</summary>
    public static double[] Speeds(ReferenceFrameSeries frames, IReadOnlyList<int> cuts)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(cuts);
        var cutSet = cuts.ToHashSet();
        var speeds = new double[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames.Moved[i] && !cutSet.Contains(i))
            {
                speeds[i] = Math.Sqrt((frames.ShiftX[i] * frames.ShiftX[i]) + (frames.ShiftY[i] * frames.ShiftY[i])) / MotionCheckLimits.FrameWidth * frames.Fps;
            }
        }

        return speeds;
    }
}
