using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Chat;
using TMConcept.Core.Engines;
using TMConcept.Core.Media;
using TMConcept.Core.Plans;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;

namespace TMConcept.Core.Videos;

/// <summary>
/// The quality modes of a video run (F11 + F14, 0.1.50; the developer's decision S2 of 24 Nov 2026). Fast (the default): nothing, the video
/// is made as before. Standard: after the render ffmpeg lays the start, middle and end of every scene and its beats out as one sheet
/// (<see cref="IPlanSheet"/>, on the processor), the quality check step's model reviews it in ONE call (blocker / polish findings) and the
/// final critic scores it in ONE call (headings 1–5, below <see cref="QualityLimits.CriticPass"/> "does not pass"); a report only. Careful: the
/// same with repair rounds (<see cref="QualityLoop"/>, at most <see cref="QualityLimits.MaxRounds"/> reviews, only blockers after
/// <see cref="QualityLimits.PolishRounds"/>, stop when the list does not shrink): when the app draws the video every faulty scene's plan is
/// asked again (one small plan call per scene, the code model pays) and the video is rendered again; the model's free code is only warned
/// about. Before the render the careful mode also draws the recurring character's sheet and compares the pictures with it (F14), and pictures
/// made on this computer reviewed by a local model are made again in rounds (the TODO item of 16 Nov 2026). Nothing here ever stops a video: a
/// review that cannot run is logged and skipped. The findings go into the version's report and the activity panel.
/// </summary>
public sealed class QualityStep(IQualityReviewer reviewer, IPlanSheet sheets, ILogger<QualityStep> logger)
{
    /// <summary>
    /// Reviews the rendered video of <paramref name="job"/> (and repairs it in the careful mode). Returns the render to save: a repaired video
    /// is rendered into a new file of the work folder, the outcome names it. Nothing when the mode is Fast or there is no reviewer.
    /// </summary>
    public async Task<RenderOutcome> AfterRenderAsync(RenderQualityJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var options = job.Options;
        var style = options.Style;
        if (!QualityRules.Reviews(style) || job.Reviewer is not { } model)
        {
            if (QualityRules.Reviews(style))
            {
                // The step was planned (a reviewer was chosen) but could not be made ready: its row ends here, not left waiting.
                job.Steps.Finish(VideoSteps.Quality);
            }

            return job.Render;
        }

        var steps = job.Steps;
        var report = job.Report;
        var outcome = job.Render;
        var input = outcome.Input;
        var subtitles = VideoStyleRules.SubtitlesOn(options);
        var call = new QualityCall(model, steps, job.VideoId);
        var drawn = PlanDrawer.Draws(job.Engine.Kind, style);
        var careful = QualityRules.Repairs(style);
        var folder = Path.Combine(job.Work, "quality");
        Directory.CreateDirectory(folder);
        string? lastSheet = null;
        IReadOnlyList<QualityFrame> lastFrames = [];
        steps.Report(VideoSteps.Quality, 0.05);

        async Task<IReadOnlyList<QualityFinding>?> ReviewAsync(int round, CancellationToken token)
        {
            var frames = QualityRules.Moments(input);
            var sheet = Path.Combine(folder, "sheet-" + round.ToString(CultureInfo.InvariantCulture) + ".png");
            var columns = input.Width > input.Height ? PlanPreviewLimits.ColumnsLandscape : PlanPreviewLimits.ColumnsPortrait;
            steps.Report(VideoSteps.Quality, Math.Min(0.9, 0.1 + ((round - 1) * 0.25)), "review " + round.ToString(CultureInfo.InvariantCulture));
            try
            {
                if (!await sheets.SheetAsync(outcome.Request.Output, frames.Select(f => f.Seconds).ToList(), columns, sheet, token).ConfigureAwait(false)
                    || !File.Exists(sheet))
                {
                    logger.LogWarning(LogEvents.OnScreen, "Quality check: the frame sheet could not be made; the review is skipped");
                    return null;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                logger.LogWarning(LogEvents.OnScreen, "Quality check: the frame sheet could not be made ({Error}); the review is skipped", ex.Message);
                return null;
            }

            lastSheet = sheet;
            lastFrames = frames;
            var found = await SafeAsync("frame review", () => reviewer.FramesAsync(call, sheet, input, frames, subtitles, token), null).ConfigureAwait(false);
            if (found is not null)
            {
                Log("frames", round, found);
            }

            return found;
        }

        async Task<IReadOnlyList<int>> RepairAsync(IReadOnlyList<QualityFinding> counted, int round, CancellationToken token)
        {
            var codeCall = new QualityCall(job.CodeModel, steps, job.VideoId);
            var items = new List<PlanItem>();
            foreach (var scene in QualityRules.Scenes(counted))
            {
                var request = new QualityRepairRequest(codeCall, input, style, scene, job.Spans.ElementAtOrDefault(scene - 1),
                    job.ImagePrompts.ElementAtOrDefault(scene - 1) ?? string.Empty, counted.Where(f => f.Scene == scene).ToList());
                var item = await SafeAsync("repair of scene " + scene.ToString(CultureInfo.InvariantCulture), () => reviewer.RepairAsync(request, token), null)
                    .ConfigureAwait(false);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            if (items.Count == 0)
            {
                return [];
            }

            // A repaired render goes into a new file: a failure keeps the video that was reviewed (its plans and its project as they were).
            var kept = input.Scenes.Select(s => s.Plan).ToList();
            SceneFixer.NewPlans(input, items, job.Spans);
            var repaired = outcome.Request with
            {
                Input = RenderInputBuilder.ForEngine(input, subtitles),
                Output = Path.Combine(job.Work, "silent-q" + (round + 1).ToString(CultureInfo.InvariantCulture) + ".mp4"),
            };
            try
            {
                steps.Report(VideoSteps.Quality, Math.Min(0.9, 0.2 + ((round - 1) * 0.25)), "repair " + round.ToString(CultureInfo.InvariantCulture));
                job.Engine.WriteProject(repaired);
                await job.Engine.RenderAsync(repaired, null, token).ConfigureAwait(false);
            }
            catch (EngineException ex)
            {
                logger.LogWarning(LogEvents.OnScreen, "Quality check: the repaired scenes could not be rendered ({Error}); the reviewed video is kept", ex.Message);
                for (var i = 0; i < kept.Count; i++)
                {
                    input.Scenes[i].Plan = kept[i];
                }

                job.Engine.WriteProject(outcome.Request with { Input = RenderInputBuilder.ForEngine(input, subtitles) });
                return [];
            }

            outcome = outcome with { Request = repaired };
            var scenes = items.Select(i => i.Scene).Distinct().Order().ToList();
            logger.LogInformation(LogEvents.OnScreen, "Quality check: scenes {Scenes} planned again and the video rendered again (round {Round})",
                string.Join(", ", scenes), round);
            return scenes;
        }

        var repair = careful && drawn ? RepairAsync : (Func<IReadOnlyList<QualityFinding>, int, CancellationToken, Task<IReadOnlyList<int>>>?)null;
        var (rounds, stop) = await QualityLoop.RunAsync(ReviewAsync, repair, cancellationToken).ConfigureAwait(false);
        report.Frames = rounds;
        report.FrameStop = stop;
        if (careful && !drawn && rounds.Any(r => r.Findings.Count > 0))
        {
            // The model's own code is not sent back for a repair: the faults are named, the video is kept.
            report.NotRepaired = true;
            report.FrameStop = QualityStops.NoRepair;
            logger.LogWarning(LogEvents.OnScreen, "Quality check: the faults of a video in free code are not repaired, only reported");
        }

        if (lastSheet is { } final)
        {
            steps.Report(VideoSteps.Quality, 0.95, "critic");
            report.Critic = [.. await SafeAsync("final critic", () => reviewer.CriticAsync(call, final, input, lastFrames, subtitles, cancellationToken),
                (IReadOnlyList<CriticScore>)[]).ConfigureAwait(false)];
            report.Files.Add(final);
            var failing = report.Critic.Where(c => c.Fails).ToList();
            if (failing.Count > 0)
            {
                logger.LogWarning(LogEvents.OnScreen, "Final critic: {Count} headings do not pass ({Headings})", failing.Count,
                    string.Join(", ", failing.Select(c => c.Heading + " " + c.Score.ToString(CultureInfo.InvariantCulture))));
            }
            else if (report.Critic.Count > 0)
            {
                logger.LogInformation(LogEvents.OnScreen, "Final critic: every heading passes (lowest {Score})", report.Critic.Min(c => c.Score));
            }
        }

        logger.LogInformation("Quality check ({Mode}): {Rounds} rounds, ended {Stop}", QualityRules.Mode(style), rounds.Count, report.FrameStop);
        steps.Finish(VideoSteps.Quality);
        return outcome;
    }

    /// <summary>
    /// The careful mode's look at the scene pictures before the render: the character sheet and its comparison (F14, when the image prompt call
    /// described a recurring character), then, when the pictures and the reviewer both run on this computer, review → make the faulty pictures
    /// again → review, in rounds (<see cref="QualityLoop"/>; the character's differences join the first round). Nothing in the other modes.
    /// </summary>
    public async Task PicturesAsync(PictureQualityJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var report = job.Report;
        if (report.Mode != QualityMode.Careful || job.Pictures.Count == 0)
        {
            return;
        }

        var call = new QualityCall(job.Reviewer, job.Steps, job.VideoId);
        report.Character = job.Character;
        IReadOnlyList<QualityFinding> different = [];
        if (job.Character.Length > 0 && job.CharacterSheet is { } draw)
        {
            job.Steps.Report(VideoSteps.Images, 1, "character sheet");
            if (await draw(cancellationToken).ConfigureAwait(false) is { } sheet && File.Exists(sheet))
            {
                report.CharacterSheet = true;
                report.Files.Add(sheet);
                different = await SafeAsync("character comparison", () => reviewer.CharacterAsync(call, sheet, job.Character, job.Pictures, job.Scenes,
                    cancellationToken), null).ConfigureAwait(false) ?? [];
                report.CharacterMismatch = [.. QualityRules.Scenes(different)];
                if (different.Count > 0)
                {
                    logger.LogWarning(LogEvents.OnScreen, "Character sheet: the pictures of scenes {Scenes} do not match it",
                        string.Join(", ", report.CharacterMismatch));
                }
            }
        }

        if (!job.Loop)
        {
            return;
        }

        var redrawn = new HashSet<int>();

        async Task<IReadOnlyList<QualityFinding>?> ReviewAsync(int round, CancellationToken token)
        {
            job.Steps.Report(VideoSteps.Images, 1, "picture review " + round.ToString(CultureInfo.InvariantCulture));
            var found = await SafeAsync("picture review", () => reviewer.PicturesAsync(call, job.Pictures, job.Scenes, token), null).ConfigureAwait(false);
            if (found is null)
            {
                return null;
            }

            Log("pictures", round, found);
            return round == 1 ? [.. found, .. different.Where(d => !found.Any(f => f.Scene == d.Scene && f.Problem == d.Problem))] : found;
        }

        async Task<IReadOnlyList<int>> RedrawAsync(IReadOnlyList<QualityFinding> counted, int round, CancellationToken token)
        {
            var scenes = await SafeAsync("pictures made again", () => job.Redraw(counted, token), (IReadOnlyList<int>)[]).ConfigureAwait(false);
            redrawn.UnionWith(scenes);
            return scenes;
        }

        var (rounds, stop) = await QualityLoop.RunAsync(ReviewAsync, RedrawAsync, cancellationToken).ConfigureAwait(false);
        report.Pictures = rounds;
        report.PictureStop = stop;

        // A picture made again for its character is no longer the one the sheet was compared with.
        report.CharacterMismatch = report.CharacterMismatch.Where(s => !redrawn.Contains(s)).ToList();
        logger.LogInformation("Picture review: {Rounds} rounds, ended {Stop}, {Redrawn} pictures made again", rounds.Count, stop, redrawn.Count);
    }

    /// <summary>
    /// One call of the quality mode: a failure of the reviewer, the repair or a picture made again (a provider's error, an unreachable local
    /// server, a failed picture) is logged and gives <paramref name="fallback"/>, so an optional check never stops a paid video; what the call
    /// was charged is already spent by the reviewer. A cancel goes up.
    /// </summary>
    private async Task<T> SafeAsync<T>(string what, Func<Task<T>> call, T fallback)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            logger.LogWarning(LogEvents.OnScreen, "Quality check: the {What} could not run ({Error}); it is skipped, the video is kept", what, ex.Message);
            return fallback;
        }
    }

    private void Log(string what, int round, IReadOnlyList<QualityFinding> found)
    {
        var blockers = found.Count(f => f.Severity == QualitySeverity.Blocker);
        if (found.Count == 0)
        {
            logger.LogInformation(LogEvents.OnScreen, "Quality check ({What}, round {Round}): nothing found", what, round);
            return;
        }

        // Only the counts and the scenes: the findings' words may quote the concept (they are in the version's report and on the Produced page).
        logger.LogWarning(LogEvents.OnScreen, "Quality check ({What}, round {Round}): {Blockers} blockers, {Polish} polish in scenes {Scenes}", what, round,
            blockers, found.Count - blockers, string.Join(", ", QualityRules.Scenes(found)));
    }
}
