using TMConcept.Core.Engines;
using TMConcept.Core.Plans;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;

namespace TMConcept.Core.Videos;

/// <summary>
/// The rules of the plan preview (F10, 0.1.49): when a video waits (the app's drawer, the template's <see cref="VideoStyle.PlanReview"/>, a
/// page that answers and night mode off; a template without it is made exactly as before), the key moments of the sheet and the small,
/// slow copy of the input the preview renders.
/// </summary>
public static class PlanReviewRules
{
    /// <summary>Whether the video waits for its plan to be approved.</summary>
    public static bool Waits(EngineKind engine, VideoStyle? style, IPlanReview? review) =>
        PlanDrawer.Draws(engine, style) && style!.PlanReview && review is { NightMode: false };

    /// <summary>Whether the video also waits after its first scene ("one scene first"; only with <see cref="Waits"/>).</summary>
    public static bool FirstScene(EngineKind engine, VideoStyle? style, IPlanReview? review) =>
        Waits(engine, style, review) && style!.FirstSceneReview;

    /// <summary>
    /// The key moments of the planned <paramref name="input"/> (seconds): one per beat, <see cref="PlanPreviewLimits.BeatShare"/> into it (at
    /// most <see cref="PlanPreviewLimits.BeatMaxSeconds"/>); the middle of a scene without beats. At most <paramref name="max"/>, evenly chosen
    /// when there are more, always inside the video.
    /// </summary>
    public static IReadOnlyList<double> KeyMoments(RenderInput input, int max = PlanPreviewLimits.MaxFrames)
    {
        ArgumentNullException.ThrowIfNull(input);
        var moments = new List<double>();
        foreach (var scene in input.Scenes)
        {
            var end = scene.StartSeconds + scene.DurationSeconds;
            var beats = scene.Plan?.Beats ?? [];
            if (beats.Count == 0)
            {
                moments.Add(scene.StartSeconds + (scene.DurationSeconds / 2));
                continue;
            }

            foreach (var beat in beats)
            {
                var at = beat.Start + Math.Min(beat.Duration * PlanPreviewLimits.BeatShare, PlanPreviewLimits.BeatMaxSeconds);
                moments.Add(Math.Clamp(at, scene.StartSeconds, Math.Max(scene.StartSeconds, end - 0.05)));
            }
        }

        var last = Math.Max(0, input.DurationSeconds - 0.05);
        moments = moments.Select(m => Math.Round(Math.Clamp(m, 0, last), 3)).ToList();
        if (moments.Count <= max || max <= 0)
        {
            return moments;
        }

        return Enumerable.Range(0, max).Select(i => moments[(int)Math.Round(i * (moments.Count - 1) / (double)(max - 1))]).ToList();
    }

    /// <summary>The scenes as the preview lists them.</summary>
    public static IReadOnlyList<PlanPreviewScene> Scenes(RenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Scenes.Select(s => new PlanPreviewScene(s.Number, s.Caption, s.Plan?.Base ?? (s.Image.Length > 0 ? PlanBases.Image : PlanBases.Title),
            s.StartSeconds)).ToList();
    }

    /// <summary>
    /// The copy of <paramref name="input"/> the preview renders: <see cref="PlanPreviewLimits.ShortSide"/> pixels on its short side (even sizes),
    /// <see cref="PlanPreviewLimits.Fps"/> frames a second, the same seconds; with <paramref name="scenes"/> only the first that many scenes (the
    /// first scene starts at 0, so nothing moves). The plans are copied with it; the input itself is not changed.
    /// </summary>
    public static RenderInput PreviewInput(RenderInput input, int scenes = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(input);
        var copy = RenderInputBuilder.FromJson(RenderInputBuilder.ToJson(input)) ?? throw new InvalidOperationException("The render input could not be copied.");
        if (scenes < copy.Scenes.Count)
        {
            copy.Scenes = copy.Scenes.Take(Math.Max(1, scenes)).ToList();
            var lastScene = copy.Scenes[^1];
            copy.DurationSeconds = Math.Round(lastScene.StartSeconds + lastScene.DurationSeconds, 3);
        }

        var shortSide = Math.Max(1, Math.Min(copy.Width, copy.Height));
        var scale = Math.Min(1.0, PlanPreviewLimits.ShortSide / (double)shortSide);
        copy.Width = Even(copy.Width * scale);
        copy.Height = Even(copy.Height * scale);
        const int fps = PlanPreviewLimits.Fps;
        copy.Fps = fps;
        foreach (var scene in copy.Scenes)
        {
            scene.StartFrame = (int)Math.Round(scene.StartSeconds * fps);
            scene.FrameCount = Math.Max(1, (int)Math.Round(scene.DurationSeconds * fps));
        }

        copy.TotalFrames = Math.Max(1, (int)Math.Round(copy.DurationSeconds * fps));
        return copy;
    }

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);
}
