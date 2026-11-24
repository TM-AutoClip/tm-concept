using TMConcept.Core.Render;
using TMConcept.Core.Styles;

namespace TMConcept.Core.Videos;

/// <summary>
/// The rules of the quality modes (F11 + F14, 0.1.50; ours): the template's mode, the moments of the review sheet (the start, middle and end
/// of every scene and its beats), which findings count in a round (after <see cref="QualityLimits.PolishRounds"/> rounds only blockers) and the
/// warnings a report gives.
/// </summary>
public static class QualityRules
{
    /// <summary>Moment name on the sheet: the start of a scene.</summary>
    public const string Start = "start";

    /// <summary>The middle of a scene.</summary>
    public const string Middle = "middle";

    /// <summary>The end of a scene.</summary>
    public const string End = "end";

    /// <summary>A beat of the scene plan.</summary>
    public const string Beat = "beat";

    /// <summary>The template's quality mode (none = <see cref="QualityMode.Fast"/>, the video as it was made before).</summary>
    public static QualityMode Mode(VideoStyle? style) => style?.Quality ?? QualityMode.Fast;

    /// <summary>Whether the mode reviews at all.</summary>
    public static bool Reviews(VideoStyle? style) => Mode(style) != QualityMode.Fast;

    /// <summary>Whether the mode repairs (careful).</summary>
    public static bool Repairs(VideoStyle? style) => Mode(style) == QualityMode.Careful;

    /// <summary>
    /// The frames of the review sheet, in time order: every scene's start and end (<see cref="QualityLimits.EdgeSeconds"/> inside it) and middle,
    /// and the key moment of each of its beats (as the plan preview takes them). More than <paramref name="max"/>: the beats are thinned first,
    /// then all frames evenly, so every scene keeps a frame as long as there is room.
    /// </summary>
    public static IReadOnlyList<QualityFrame> Moments(RenderInput input, int max = QualityLimits.MaxFrames)
    {
        ArgumentNullException.ThrowIfNull(input);
        var last = Math.Max(0, input.DurationSeconds - 0.05);
        var fixedFrames = new List<QualityFrame>();
        var beats = new List<QualityFrame>();
        foreach (var scene in input.Scenes)
        {
            var from = scene.StartSeconds;
            var to = scene.StartSeconds + scene.DurationSeconds;
            var edge = Math.Min(QualityLimits.EdgeSeconds, scene.DurationSeconds / 4);
            fixedFrames.Add(new QualityFrame(scene.Number, Round(Math.Clamp(from + edge, 0, last)), Start));
            fixedFrames.Add(new QualityFrame(scene.Number, Round(Math.Clamp(from + (scene.DurationSeconds / 2), 0, last)), Middle));
            fixedFrames.Add(new QualityFrame(scene.Number, Round(Math.Clamp(to - edge, 0, last)), End));
            foreach (var beat in scene.Plan?.Beats ?? [])
            {
                var at = beat.Start + Math.Min(beat.Duration * PlanPreviewLimits.BeatShare, PlanPreviewLimits.BeatMaxSeconds);
                beats.Add(new QualityFrame(scene.Number, Round(Math.Clamp(Math.Clamp(at, from, Math.Max(from, to - 0.05)), 0, last)), Beat));
            }
        }

        // A beat on (almost) the same moment as a fixed frame shows nothing new.
        beats = beats.Where(b => !fixedFrames.Any(f => f.Scene == b.Scene && Math.Abs(f.Seconds - b.Seconds) < 0.3)).ToList();
        if (max <= 0 || fixedFrames.Count + beats.Count <= max)
        {
            return Order(fixedFrames.Concat(beats));
        }

        return fixedFrames.Count <= max
            ? Order(fixedFrames.Concat(Even(beats, max - fixedFrames.Count)))
            : Even(Order(fixedFrames), max);
    }

    /// <summary>The findings that count in <paramref name="round"/>: all of them first, only the blockers after <see cref="QualityLimits.PolishRounds"/> rounds.</summary>
    public static IReadOnlyList<QualityFinding> Counted(IReadOnlyList<QualityFinding> findings, int round)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return round > QualityLimits.PolishRounds ? findings.Where(f => f.Severity == QualitySeverity.Blocker).ToList() : findings;
    }

    /// <summary>The scenes of <paramref name="findings"/>, each once, in order.</summary>
    public static IReadOnlyList<int> Scenes(IEnumerable<QualityFinding> findings) => findings.Select(f => f.Scene).Distinct().Order().ToList();

    /// <summary>
    /// The warnings of a report (yellow on the video): a blocker left in the last frame or picture round (or a picture that does not match the
    /// character), only polish left, a critic's heading below <see cref="QualityLimits.CriticPass"/>, careful-mode faults the free code could not be
    /// repaired for.
    /// </summary>
    public static IReadOnlyList<string> Warnings(QualityReport? report)
    {
        if (report is null)
        {
            return [];
        }

        var left = (report.Frames.LastOrDefault()?.Findings ?? []).Concat(report.Pictures.LastOrDefault()?.Findings ?? []).ToList();
        var warnings = new List<string>();
        if (left.Any(f => f.Severity == QualitySeverity.Blocker) || report.CharacterMismatch.Count > 0)
        {
            warnings.Add(VideoWarnings.QualityBlocker);
        }
        else if (left.Count > 0)
        {
            warnings.Add(VideoWarnings.QualityPolish);
        }

        if (report.Critic.Any(c => c.Fails))
        {
            warnings.Add(VideoWarnings.QualityFails);
        }

        if (report.NotRepaired)
        {
            warnings.Add(VideoWarnings.QualityNotRepaired);
        }

        return warnings;
    }

    private static double Round(double seconds) => Math.Round(seconds, 3);

    private static List<QualityFrame> Order(IEnumerable<QualityFrame> frames) => frames.OrderBy(f => f.Seconds).ThenBy(f => f.Scene).ToList();

    private static List<QualityFrame> Even(IReadOnlyList<QualityFrame> frames, int count)
    {
        if (count <= 0 || frames.Count == 0)
        {
            return [];
        }

        if (frames.Count <= count)
        {
            return [.. frames];
        }

        return count == 1
            ? [frames[0]]
            : Enumerable.Range(0, count).Select(i => frames[(int)Math.Round(i * (frames.Count - 1) / (double)(count - 1))]).Distinct().ToList();
    }
}
