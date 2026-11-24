using System.Globalization;
using System.Text;
using TMConcept.Core.Styles;

namespace TMConcept.Core.Media;

/// <summary>
/// The level of the background music over the video (0.1.44, C3 + F4; pure, tests read it): normal where nobody speaks, ducked under the
/// narration. The narration parts come from the clips' starts (the mux's <c>adelay</c>) and the spoken parts inside each clip
/// (<see cref="Speech.NarrationSilence.Spoken"/>); parts closer than <see cref="MusicMixDefaults.MergeGapMs"/> are joined first
/// (<see cref="Merge"/>), so the music does not rise in a breath. Each part ducks the music over <see cref="MusicMixDefaults.AttackMs"/>
/// before it starts and lets it back over <see cref="MusicMixDefaults.ReleaseMs"/> after it ends. Joined parts are at least
/// <see cref="MusicMixDefaults.MergeGapMs"/> apart, longer than both ramps, so the ramps of two parts never meet. The same shape is given as
/// numbers (<see cref="Gain"/>) and as an ffmpeg <c>volume</c> expression of <c>t</c> evaluated per frame (<see cref="Expression"/>); a
/// predictable envelope rather than <c>sidechaincompress</c>.
/// </summary>
public static class MusicEnvelope
{
    /// <summary>A level in dB as a factor (0 dB = 1).</summary>
    public static double Linear(double db) => Math.Pow(10, db / 20);

    /// <summary>
    /// The narration parts of every clip on the video's time line: each clip's spoken parts (milliseconds in the clip) moved by the clip's
    /// start, in order.
    /// </summary>
    public static IReadOnlyList<NarrationSpan> Timeline(IEnumerable<(long StartMs, IReadOnlyList<NarrationSpan> Spoken)> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        return clips.SelectMany(c => c.Spoken.Select(s => new NarrationSpan(c.StartMs + s.StartMs, c.StartMs + s.EndMs)))
            .Where(s => s.EndMs > s.StartMs).OrderBy(s => s.StartMs).ToList();
    }

    /// <summary>Joins parts that overlap or lie closer than <paramref name="gapMs"/> (in order of their start).</summary>
    public static IReadOnlyList<NarrationSpan> Merge(IEnumerable<NarrationSpan> spans, int gapMs = MusicMixDefaults.MergeGapMs)
    {
        ArgumentNullException.ThrowIfNull(spans);
        var merged = new List<NarrationSpan>();
        foreach (var span in spans.Where(s => s.EndMs > s.StartMs).OrderBy(s => s.StartMs).ThenBy(s => s.EndMs))
        {
            if (merged.Count > 0 && span.StartMs - merged[^1].EndMs < gapMs)
            {
                merged[^1] = merged[^1] with { EndMs = Math.Max(merged[^1].EndMs, span.EndMs) };
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged;
    }

    /// <summary>
    /// The music's factor at <paramref name="seconds"/>: <paramref name="level"/> where nobody speaks, <paramref name="ducked"/> under a part of
    /// <paramref name="merged"/>, straight ramps at the edges.
    /// </summary>
    public static double Gain(IReadOnlyList<NarrationSpan> merged, double seconds, double level, double ducked)
    {
        ArgumentNullException.ThrowIfNull(merged);
        var attack = MusicMixDefaults.AttackMs / 1000.0;
        var release = MusicMixDefaults.ReleaseMs / 1000.0;
        var depth = 0.0;
        foreach (var span in merged)
        {
            var down = Math.Clamp((seconds - ((span.StartMs / 1000.0) - attack)) / attack, 0, 1);
            var up = Math.Clamp(((span.EndMs / 1000.0) + release - seconds) / release, 0, 1);
            depth += Math.Min(down, up);
        }

        return level - ((level - ducked) * Math.Min(1, depth));
    }

    /// <summary>
    /// <see cref="Gain"/> as an ffmpeg expression of <c>t</c> (seconds) for <c>volume=…:eval=frame</c>: a constant without narration, else
    /// the level less the ducking times the sum of each part's ramp (one term per part, no nesting, so a long video does not make a deep
    /// expression).
    /// </summary>
    public static string Expression(IReadOnlyList<NarrationSpan> merged, double level, double ducked)
    {
        ArgumentNullException.ThrowIfNull(merged);
        var c = CultureInfo.InvariantCulture;
        if (merged.Count == 0 || level <= ducked)
        {
            return Number(merged.Count == 0 ? level : Math.Min(level, ducked));
        }

        var attack = MusicMixDefaults.AttackMs / 1000.0;
        var release = MusicMixDefaults.ReleaseMs / 1000.0;
        var text = new StringBuilder();
        text.Append(Number(level)).Append('-').Append(Number(level - ducked)).Append("*min(1,");
        for (var i = 0; i < merged.Count; i++)
        {
            if (i > 0)
            {
                text.Append('+');
            }

            var from = (merged[i].StartMs / 1000.0) - attack;
            var to = (merged[i].EndMs / 1000.0) + release;
            text.Append(c, $"min(clip((t{Minus(from)})/{Seconds(attack)},0,1),clip(({Seconds(to)}-t)/{Seconds(release)},0,1))");
        }

        return text.Append(')').ToString();
    }

    /// <summary>"−x" written after <c>t</c> (a part that starts at the very beginning ramps from before 0: "t+0.15").</summary>
    private static string Minus(double value) => value < 0 ? "+" + Seconds(-value) : "-" + Seconds(value);

    private static string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Number(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
}
