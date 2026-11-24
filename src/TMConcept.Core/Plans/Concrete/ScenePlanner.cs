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
    public async Task<int> PlanAsync(PlanRequest request, VideoStepProgress steps, CancellationToken cancellationToken)
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
        var system = PlanPromptBuilder.System(motion, tempo, family);
        var user = PlanPromptBuilder.User(input.Title, input.Language, request.Style, scenes);
        var model = request.CodeModel;
        var chat = chats.Create(model.Model, model.Key.Secret);

        async Task<string> AskAsync(string text, CancellationToken token)
        {
            try
            {
                var answer = await chat.SendAsync(new ChatRequest(system, text, CacheSystem: true, Schema: PlanPromptBuilder.Schema(), MaxOutputTokens: 16000,
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

        // The structure and the plan rules (0.1.41) share the single repair; a repair that still breaks a rule is used as far as it goes.
        var check = new PlanCheck(scenes, input.Scenes.Select(s => s.Image.Length > 0).ToList(), request.Subtitles, tempo, motion);
        var problems = PlanReader.Problems(items, check);
        if (problems.Count > 0)
        {
            logger.LogWarning("The scene plan does not fit the {Count} scenes or the plan rules ({Problems}); asking for one repair", scenes.Count,
                string.Join("; ", problems));
            var repaired = PlanReader.Read(await AskAsync(PlanPromptBuilder.Repair(user, problems), cancellationToken).ConfigureAwait(false));
            if (PlanReader.Better(repaired, items, check))
            {
                items = repaired;
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
            await PointsAsync(request, points, cleaned, steps, cancellationToken).ConfigureAwait(false);
        }

        for (var i = 0; i < input.Scenes.Count; i++)
        {
            var scene = input.Scenes[i];
            var plan = cleaned[i].Plan;
            var spans = i < request.Spans.Count ? request.Spans[i] : null;
            PlanTiming.Apply(plan, scene.StartSeconds, scene.DurationSeconds, PlanTiming.Starts(scenes[i].Sentences, spans, scene.DurationSeconds));
            scene.Plan = plan;
        }

        logger.LogInformation("Scene plan: {Bases}; {Beats} beats; transitions {Transitions}", string.Join(", ", cleaned.GroupBy(c => c.Plan.Base).Select(g => g.Key + " " + g.Count())),
            cleaned.Sum(c => c.Plan.Beats.Count), string.Join(", ", cleaned.Skip(1).Select(c => c.Plan.Transition.Length > 0 ? c.Plan.Transition : "-")));
        return missing;
    }

    /// <summary>Places the image scenes' points where the image points model sees them (one call per picture; a failure keeps the guesses).</summary>
    private async Task PointsAsync(PlanRequest request, ResolvedModel model, IReadOnlyList<PlanItem> plans, VideoStepProgress steps,
        CancellationToken cancellationToken)
    {
        var chat = chats.Create(model.Model, model.Key.Secret);
        var placed = 0;
        for (var i = 0; i < plans.Count && i < request.Input.Scenes.Count; i++)
        {
            var plan = plans[i].Plan;
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
