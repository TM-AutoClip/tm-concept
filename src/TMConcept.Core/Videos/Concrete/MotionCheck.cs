using TMConcept.Core.Render;

namespace TMConcept.Core.Videos;

/// <summary>
/// The motion check of a finished video (F3, 0.1.41; the idea and the thresholds of ReelMimic's video-clone motion_check.py, MIT, written
/// again here): the numbers of every frame (<see cref="MotionFrameReader"/>) are searched for what a single frame cannot show — a cut inside
/// a scene, a freeze where the plan expects motion, black frames inside the film, a short white flash, a hard cut to black at the end and a
/// planned fade that did not happen. Pure functions; the caller reads the frames and keeps the findings as yellow warnings (never stops a
/// video).
/// </summary>
public static class MotionCheck
{
    /// <summary>
    /// What the plan of <paramref name="input"/> tells the check: every scene start after the first, the planned fades, and where the picture
    /// stands still on purpose: the closing card, and in a scene drawn by the app (its plan) everything outside its beats' first second (a
    /// title card, a map or a diagram between its beats, a picture's slow drift).
    /// </summary>
    public static MotionExpectations Expect(RenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var starts = input.Scenes.Where(s => s.Number > 1).Select(s => s.StartSeconds).ToList();
        var fades = VideoAssembler.Fades(input).Select(ms => ms / 1000.0).ToList();
        var still = new List<(double From, double To)>();
        if (input.Style is { EndCardSeconds: > 0 } style)
        {
            var card = Math.Max(0, input.DurationSeconds - style.EndCardSeconds);
            starts.Add(card);
            still.Add((card, input.DurationSeconds));
        }

        foreach (var scene in input.Scenes.Where(s => s.Plan is not null))
        {
            var end = scene.StartSeconds + scene.DurationSeconds;
            var moves = scene.Plan!.Beats.Where(b => b.Camera is not null || b.Actions.Count > 0)
                .Select(b => (From: b.Start, To: b.Start + MotionCheckLimits.BeatMotionSeconds)).Prepend((From: scene.StartSeconds, To: scene.StartSeconds))
                .OrderBy(m => m.From).ToList();
            var at = scene.StartSeconds;
            foreach (var (from, to) in moves)
            {
                if (from > at)
                {
                    still.Add((at, Math.Min(from, end)));
                }

                at = Math.Max(at, to);
            }

            if (at < end)
            {
                still.Add((at, end));
            }
        }

        return new MotionExpectations(starts, fades, still);
    }

    /// <summary>The findings of <paramref name="series"/> in time order (empty = nothing to look at).</summary>
    public static IReadOnlyList<MotionFinding> Find(MotionSeries series, MotionExpectations expect)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(expect);
        var findings = new List<MotionFinding>();
        if (series.Count < 3)
        {
            return findings;
        }

        Cuts(series, expect, findings);
        Freezes(series, expect, findings);
        Blacks(series, expect, findings);
        Flashes(series, findings);
        EndBlack(series, findings);
        Fades(series, expect, findings);
        return findings.OrderBy(f => f.From).ThenBy(f => f.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Most of the frame changing a lot at once, away from a scene start (or the closing card's start) and a fade, and not into or out of a
    /// black or white frame.
    /// </summary>
    private static void Cuts(MotionSeries series, MotionExpectations expect, List<MotionFinding> findings)
    {
        var edges = expect.SceneStarts;
        for (var i = 1; i < series.Count; i++)
        {
            var t = series.Time(i);
            if (series.CutShare[i] > MotionCheckLimits.CutShare && series.Change[i] > MotionCheckLimits.CutChange
                && t > MotionCheckLimits.StartSkipSeconds
                && !edges.Any(e => Math.Abs(e - t) <= MotionCheckLimits.SceneEdgeSeconds + (1 / series.Fps))
                && !NearFade(t, expect) && !Extreme(series.Luminance[i]) && !Extreme(series.Luminance[i - 1]))
            {
                findings.Add(new MotionFinding(VideoWarnings.MotionCut, t, t));
            }
        }
    }

    /// <summary>A stretch of still frames at least a second long, of which at least half a second is where the plan expects motion.</summary>
    private static void Freezes(MotionSeries series, MotionExpectations expect, List<MotionFinding> findings)
    {
        var run = 0;
        for (var i = 1; i <= series.Count; i++)
        {
            if (i < series.Count && series.Change[i] < MotionCheckLimits.FreezeChange)
            {
                run++;
                continue;
            }

            if (run / series.Fps >= MotionCheckLimits.FreezeSeconds)
            {
                var from = series.Time(i - 1 - run);
                var to = series.Time(i - 1);
                var dark = Enumerable.Range(i - 1 - run, run + 1).Average(f => series.Luminance[f]) < MotionCheckLimits.BlackLuminance;
                if (!dark && (to - from) - Covered(from, to, expect.Still) >= MotionCheckLimits.FreezeUncoveredSeconds)
                {
                    findings.Add(new MotionFinding(VideoWarnings.MotionFreeze, from, to));
                }
            }

            run = 0;
        }
    }

    /// <summary>Black frames inside the film: not at the very start, not in a planned fade, not the end (that is the end's own finding).</summary>
    private static void Blacks(MotionSeries series, MotionExpectations expect, List<MotionFinding> findings)
    {
        foreach (var (first, last) in Runs(series, l => l < MotionCheckLimits.BlackLuminance))
        {
            var from = series.Time(first);
            var to = series.Time(last);
            if (from > MotionCheckLimits.StartSkipSeconds && last < series.Count - 1 && !expect.Fades.Any(f => Overlaps(from, to, f)))
            {
                findings.Add(new MotionFinding(VideoWarnings.MotionBlack, from, to));
            }
        }
    }

    /// <summary>White frames for at most half a second (a longer white stretch is a white card).</summary>
    private static void Flashes(MotionSeries series, List<MotionFinding> findings)
    {
        foreach (var (first, last) in Runs(series, l => l > MotionCheckLimits.WhiteLuminance))
        {
            if ((last - first + 1) / series.Fps <= MotionCheckLimits.FlashSeconds)
            {
                findings.Add(new MotionFinding(VideoWarnings.MotionFlash, series.Time(first), series.Time(last)));
            }
        }
    }

    /// <summary>The last frame is black and the last half second drops into it at once from a lit picture (no fade out).</summary>
    private static void EndBlack(MotionSeries series, List<MotionFinding> findings)
    {
        if (series.Count <= series.Fps)
        {
            return;
        }

        var tail = (int)Math.Round(series.Fps * MotionCheckLimits.EndTailSeconds);
        var start = series.Count - tail;
        if (tail <= 3 || series.Luminance[^1] >= MotionCheckLimits.BlackLuminance || series.Luminance[start] <= MotionCheckLimits.EndTailBright)
        {
            return;
        }

        for (var i = start + 1; i < series.Count; i++)
        {
            if (series.Luminance[i] - series.Luminance[i - 1] < -MotionCheckLimits.EndDrop)
            {
                findings.Add(new MotionFinding(VideoWarnings.MotionEndBlack, series.Time(i), series.Time(series.Count - 1)));
                return;
            }
        }
    }

    /// <summary>A planned fade whose darkest frame stays above half the brightness around it (a picture too dark to tell is skipped).</summary>
    private static void Fades(MotionSeries series, MotionExpectations expect, List<MotionFinding> findings)
    {
        const double window = MotionCheckLimits.FadeWindowSeconds;
        const double around = MotionCheckLimits.FadeReferenceSeconds;
        foreach (var fade in expect.Fades)
        {
            var inside = Frames(series, fade - window, fade + window);
            if (inside.Count == 0)
            {
                continue;
            }

            var before = Frames(series, fade - window - around, fade - window);
            var after = Frames(series, fade + window, fade + window + around);
            var sides = new[] { before, after }.Where(s => s.Count > 0).Select(s => s.Average()).ToList();
            if (sides.Count == 0)
            {
                continue;
            }

            var reference = sides.Min();
            if (reference >= MotionCheckLimits.FadeDarkReference && inside.Min() > reference * MotionCheckLimits.FadeDip)
            {
                findings.Add(new MotionFinding(VideoWarnings.MotionFade, fade, fade));
            }
        }
    }

    /// <summary>The brightness of the frames from <paramref name="from"/> up to <paramref name="to"/> (seconds).</summary>
    private static List<double> Frames(MotionSeries series, double from, double to)
    {
        var first = Math.Max(0, (int)Math.Ceiling(from * series.Fps));
        var last = Math.Min(series.Count - 1, (int)Math.Floor(to * series.Fps));
        return first > last ? [] : Enumerable.Range(first, last - first + 1).Select(i => series.Luminance[i]).ToList();
    }

    /// <summary>The first and last frame of every run of frames whose brightness passes <paramref name="test"/>.</summary>
    private static IEnumerable<(int First, int Last)> Runs(MotionSeries series, Func<double, bool> test)
    {
        var first = -1;
        for (var i = 0; i <= series.Count; i++)
        {
            if (i < series.Count && test(series.Luminance[i]))
            {
                first = first < 0 ? i : first;
                continue;
            }

            if (first >= 0)
            {
                yield return (first, i - 1);
                first = -1;
            }
        }
    }

    /// <summary>A black or white frame: a cut into or out of it is that frame's own finding (black, flash, the end), not a second one.</summary>
    private static bool Extreme(double luminance) => luminance < MotionCheckLimits.BlackLuminance || luminance > MotionCheckLimits.WhiteLuminance;

    private static bool NearFade(double t, MotionExpectations expect) => expect.Fades.Any(f => Overlaps(t, t, f));

    private static bool Overlaps(double from, double to, double fade) =>
        to >= fade - MotionCheckLimits.FadeWindowSeconds && from <= fade + MotionCheckLimits.FadeWindowSeconds;

    /// <summary>How much of <paramref name="from"/>–<paramref name="to"/> the still stretches cover (seconds; they may overlap).</summary>
    private static double Covered(double from, double to, IReadOnlyList<(double From, double To)> still)
    {
        var covered = 0.0;
        var at = from;
        foreach (var (a, b) in still.Where(s => s.To > from && s.From < to).OrderBy(s => s.From))
        {
            var start = Math.Max(a, at);
            var end = Math.Min(b, to);
            if (end > start)
            {
                covered += end - start;
                at = end;
            }
        }

        return covered;
    }
}
