using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Videos;

namespace TMConcept.Core.Plans;

/// <summary>
/// Writes the plans of a video drawn by the app (0.1.33): one paid call to the video code model (<see cref="PlanPromptBuilder"/>; an
/// unreadable answer is asked again as it was, an answer that does not fit the scenes or breaks a plan rule (<see cref="PlanReader.Rules"/>,
/// 0.1.41) gets ONE repair request), cleaned
/// (<see cref="PlanReader"/>); then, when an image points model is chosen, every image scene's points are placed where that model sees them
/// (<see cref="ImagePointPrompt"/>, one call per picture, a failure keeps the plan's guesses); then the beats are timed from the spoken
/// sentences (<see cref="PlanTiming"/>). The transitions, eases and drifts are settled without a call (<see cref="PlanMotion"/>, 0.1.42). A plan that still does not fit after the repair is used as far as it goes: a scene without a plan
/// shows its picture or its title (never a stopped video for an overlay). Every call is counted.
/// </summary>
public sealed class ScenePlanner(IChatClientFactory chats, ICostCalculator costs, ILogger<ScenePlanner> logger)
{
    /// <summary>Plans every scene of <paramref name="request"/>'s input (sets <see cref="Render.RenderScene.Plan"/>); returns the scenes without a plan of their own.</summary>
    public async Task<int> PlanAsync(PlanRequest request, VideoStepProgress steps, CancellationToken cancellationToken) =>
        (await DraftAsync(request, steps, cancellationToken).ConfigureAwait(false)).Missing;

    /// <summary>
    /// The same as <see cref="PlanAsync"/>, with the answer the plan was read from (F10, 0.1.49: a previewed plan is planned again from it and
    /// the user's notes, <see cref="PlanRequest.Notes"/>).
    /// </summary>
    public async Task<(int Missing, string Answer)> DraftAsync(PlanRequest request, VideoStepProgress steps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(steps);
        var input = request.Input;
        var scenes = input.Scenes.Select((s, i) =>
        {
            var spans = i < request.Spans.Count ? request.Spans[i] : null;
            var prompt = s.Image.Length == 0 ? string.Empty : request.ImagePrompts.ElementAtOrDefault(i) is { Length: > 0 } p ? p : s.Visual;
            return new PlanScene(s.Number, s.DurationSeconds, s.Visual, PlanTiming.Sentences(s.Caption, spans), prompt);
        }).ToList();
        var motion = request.Style?.Motion ?? Styles.MotionIntensity.Normal;
        var tempo = request.Style?.Tempo ?? Speech.NarrationTempos.Default;
        var family = request.Style is { DrawStyle: not Styles.DrawStyle.Auto } style ? Styles.DrawStyles.Of(style.DrawStyle).Transition : string.Empty;
        var system = PlanPromptBuilder.System(motion, tempo, family, request.MusicHints);
        var user = PlanPromptBuilder.User(input.Title, input.Language, request.Style, scenes);
        if (request.Notes is { Count: > 0 } notes)
        {
            // F10: all the notes of a preview go into this one call, with the plan they are about.
            user = PlanPromptBuilder.Revise(user, request.Previous, notes);
            logger.LogInformation("The scene plan is made again with {Count} notes of the user", notes.Count);
        }

        var model = request.CodeModel;
        var chat = chats.Create(model.Model, model.Key.Secret);

        async Task<string> AskAsync(string text, CancellationToken token)
        {
            try
            {
                var answer = await chat.SendAsync(new ChatRequest(system, text, CacheSystem: true, Schema: PlanPromptBuilder.Schema(request.MusicHints), MaxOutputTokens: 16000,
                    Effort: model.Effort, Stream: true), token).ConfigureAwait(false);
                Spend(steps, request.VideoId, PipelineSteps.ScenePlanCost, model, answer.Usage, answer.ReportedCostUsd);
                return answer.Text;
            }
            catch (Providers.ProviderException ex) when (ex.Code is "truncated" or "incomplete")
            {
                logger.LogWarning("The scene plan answer was cut off ({Code}); its complete scenes are used", ex.Code);
                if (ex.ChargedUsage is { } charged)
                {
                    Spend(steps, request.VideoId, PipelineSteps.ScenePlanCost, model, charged, null);
                }

                return ex.PartialText;
            }
            catch (Providers.ProviderException ex) when (ex.ChargedUsage is { } charged)
            {
                Spend(steps, request.VideoId, PipelineSteps.ScenePlanCost, model, charged, null);
                throw;
            }
        }

        steps.Report(VideoSteps.Code, 0.2);
        var first = await JsonAnswerRetry.RunAsync("Scene plan", async (_, token) =>
        {
            var text = await AskAsync(user, token).ConfigureAwait(false);
            return PlanReader.Read(text).Count > 0 ? new[] { text } : null;
        }, logger, cancellationToken).ConfigureAwait(false);
        var items = PlanReader.Read(first[0]);
        var answer = first[0];

        // The structure and the plan rules (0.1.41) share the single repair; a repair that still breaks a rule is used as far as it goes.
        var check = new PlanCheck(scenes, input.Scenes.Select(s => s.Image.Length > 0).ToList(), request.Subtitles, tempo, motion);
        var problems = PlanReader.Problems(items, check);
        if (problems.Count > 0)
        {
            logger.LogWarning("The scene plan does not fit the {Count} scenes or the plan rules ({Problems}); asking for one repair", scenes.Count,
                string.Join("; ", problems));
            var repairText = await AskAsync(PlanPromptBuilder.Repair(user, problems), cancellationToken).ConfigureAwait(false);
            var repaired = (Items: PlanReader.Read(repairText), Text: repairText);
            if (PlanReader.Better(repaired.Items, items, check))
            {
                items = repaired.Items;
                answer = repaired.Text;
                var left = PlanReader.Problems(items, check);
                if (left.Count > 0)
                {
                    logger.LogInformation("The repaired scene plan still has {Count} findings ({Problems}); it is used as far as it goes", left.Count,
                        string.Join("; ", left));
                }
            }
        }

        var missing = Enumerable.Range(1, scenes.Count).Count(n => items.All(i => i.Scene != n));
        if (missing > 0)
        {
            logger.LogWarning("{Missing} of {Count} scenes have no plan; they show their picture or their title", missing, scenes.Count);
        }

        var cleaned = PlanReader.Clean(items, scenes.Select(s => s.Sentences.Count).ToList(), input.Scenes.Select(s => s.Image.Length > 0).ToList());
        PlanMotion.Apply(cleaned, input.Scenes.Select(s => s.Transition).ToList(), tempo, family);
        steps.Report(VideoSteps.Code, 0.7);
        if (request.PointsModel is { } points)
        {
            await PointsAsync(request, points, cleaned.Select(c => (RenderPlan?)c.Plan).ToList(), steps, cancellationToken).ConfigureAwait(false);
        }

        for (var i = 0; i < input.Scenes.Count; i++)
        {
            var scene = input.Scenes[i];
            var plan = cleaned[i].Plan;
            var spans = i < request.Spans.Count ? request.Spans[i] : null;
            PlanTiming.Apply(plan, scene.StartSeconds, scene.DurationSeconds, PlanTiming.Starts(scenes[i].Sentences, spans, scene.DurationSeconds));
            scene.Plan = plan;
        }

        if (request.MusicHints)
        {
            logger.LogInformation("Scene plan music: {Hints}", string.Join(", ", cleaned.Select(c => c.Plan.Music is { } m
                ? m.Mood + "/" + m.Style + "/" + m.Tempo + (m.Start ? " start" : string.Empty) + (m.End ? " end" : string.Empty) : "-")));
        }

        logger.LogInformation("Scene plan: {Bases}; {Beats} beats; transitions {Transitions}", string.Join(", ", cleaned.GroupBy(c => c.Plan.Base).Select(g => g.Key + " " + g.Count())),
            cleaned.Sum(c => c.Plan.Beats.Count), string.Join(", ", cleaned.Skip(1).Select(c => c.Plan.Transition.Length > 0 ? c.Plan.Transition : "-")));
        return (missing, answer);
    }

    /// <summary>
    /// F10 (0.1.49): the image points of a plan made before its images (a previewed plan; <see cref="Render.RenderScene.Plan"/> is set), placed
    /// now that the images are there; nothing without an image points model. The plan's timing is not changed (the points are places, not times).
    /// </summary>
    public Task PlaceAsync(PlanRequest request, VideoStepProgress steps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(steps);
        return request.PointsModel is { } points
            ? PointsAsync(request, points, request.Input.Scenes.Select(s => s.Plan).ToList(), steps, cancellationToken)
            : Task.CompletedTask;
    }

    /// <summary>Places the image scenes' points where the image points model sees them (one call per picture; a failure keeps the guesses).</summary>
    private async Task PointsAsync(PlanRequest request, ResolvedModel model, IReadOnlyList<RenderPlan?> plans, VideoStepProgress steps,
        CancellationToken cancellationToken)
    {
        var chat = chats.Create(model.Model, model.Key.Secret);
        var placed = 0;
        for (var i = 0; i < plans.Count && i < request.Input.Scenes.Count; i++)
        {
            if (plans[i] is not { } plan)
            {
                continue;
            }

            var anchors = plan.Anchors.Where(a => a.Find.Length > 0 && a.X is not null).ToList();
            var file = Path.Combine(request.ProjectFolder, request.Input.Scenes[i].Image);
            if (plan.Base != PlanBases.Image || anchors.Count == 0 || request.Input.Scenes[i].Image.Length == 0 || !File.Exists(file))
            {
                continue;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
                var mime = Path.GetExtension(file).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png";
                var answer = await chat.SendAsync(new ChatRequest(ImagePointPrompt.System, ImagePointPrompt.User(anchors), Schema: ImagePointPrompt.Schema(),
                    MaxOutputTokens: 1500, Effort: model.Effort, Images: [new ImagePart(bytes, mime)]), cancellationToken).ConfigureAwait(false);
                Spend(steps, request.VideoId, PipelineSteps.ImagePoints, model, answer.Usage, answer.ReportedCostUsd);
                placed += ImagePointPrompt.Apply(anchors, answer.Text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex is Providers.ProviderException { ChargedUsage: { } charged })
                {
                    Spend(steps, request.VideoId, PipelineSteps.ImagePoints, model, charged, null);
                }

                logger.LogWarning("Scene {Number}'s image points could not be found ({Error}); the plan's guesses are kept", i + 1, ex.Message);
            }
        }

        logger.LogInformation("Image points: {Placed} placed by {Provider}/{Model}", placed, model.Model.Provider, model.Model.Id);
    }

    private void Spend(VideoStepProgress steps, Guid id, string step, ResolvedModel model, ChatUsage usage, decimal? reported) =>
        steps.Spend(new VideoCost { VideoId = id, Step = step, Usd = costs.Exact(model.Model, usage, reported), Exact = true });
}
