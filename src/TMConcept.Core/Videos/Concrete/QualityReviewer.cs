using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Plans;
using TMConcept.Core.Render;

namespace TMConcept.Core.Videos;

/// <summary>
/// <see cref="IQualityReviewer"/> with the chat models (F11 + F14, 0.1.50): the quality check step's model reads the sheet or the pictures
/// (<see cref="ChatRequest.Images"/>; a local model goes through the graphics card lease of the chat factory, so it waits until the image
/// model is unloaded); the code model writes a scene's new plan. Each call is spent at once with its exact price under its own cost line
/// (<see cref="PipelineSteps.QualityCheck"/>, <see cref="PipelineSteps.QualityCriticCost"/>, <see cref="PipelineSteps.QualityRepairCost"/>),
/// also a cut-off one. A provider's error goes up as it is; an answer that cannot be read is logged and returns nothing.
/// </summary>
public sealed class QualityReviewer(IChatClientFactory chats, ICostCalculator costs, ILogger<QualityReviewer> logger) : IQualityReviewer
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<QualityFinding>?> FramesAsync(QualityCall call, string sheet, RenderInput input, IReadOnlyList<QualityFrame> frames,
        bool subtitles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var answer = await AskAsync(call, PipelineSteps.QualityCheck, QualityPrompt.FramesSystem, QualityPrompt.FramesUser(input, frames, subtitles),
            QualityPrompt.FindingsSchema(), QualityLimits.ReviewOutputTokens, [sheet], cancellationToken).ConfigureAwait(false);
        return Findings("frame review", answer, input.Scenes.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QualityFinding>?> PicturesAsync(QualityCall call, IReadOnlyList<QualityPicture> pictures, int scenes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        var answer = await AskAsync(call, PipelineSteps.QualityCheck, QualityPrompt.PicturesSystem, QualityPrompt.PicturesUser(pictures),
            QualityPrompt.FindingsSchema(), QualityLimits.ReviewOutputTokens, pictures.Select(p => p.File).ToList(), cancellationToken).ConfigureAwait(false);
        return Findings("picture review", answer, scenes);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QualityFinding>?> CharacterAsync(QualityCall call, string sheet, string character, IReadOnlyList<QualityPicture> pictures,
        int scenes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        var answer = await AskAsync(call, PipelineSteps.QualityCheck, QualityPrompt.CharacterSystem, QualityPrompt.CharacterUser(character, pictures),
            QualityPrompt.FindingsSchema(), QualityLimits.ReviewOutputTokens, [sheet, .. pictures.Select(p => p.File)], cancellationToken).ConfigureAwait(false);
        return Findings("character comparison", answer, scenes);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CriticScore>> CriticAsync(QualityCall call, string sheet, RenderInput input, IReadOnlyList<QualityFrame> frames,
        bool subtitles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var answer = await AskAsync(call, PipelineSteps.QualityCriticCost, QualityPrompt.CriticSystem, QualityPrompt.FramesUser(input, frames, subtitles),
            QualityPrompt.CriticSchema(), QualityLimits.CriticOutputTokens, [sheet], cancellationToken).ConfigureAwait(false);
        var scores = QualityPrompt.ReadCritic(answer);
        if (scores.Count == 0)
        {
            logger.LogWarning("The final critic's answer could not be read; the video has no critic scores");
        }

        return scores;
    }

    /// <inheritdoc />
    public async Task<PlanItem?> RepairAsync(QualityRepairRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = request.Input;
        if (request.Scene < 1 || request.Scene > input.Scenes.Count)
        {
            return null;
        }

        var scene = input.Scenes[request.Scene - 1];
        var style = request.Style;
        var motion = style?.Motion ?? Styles.MotionIntensity.Normal;
        var tempo = style?.Tempo ?? Speech.NarrationTempos.Default;
        var family = style is { DrawStyle: not Styles.DrawStyle.Auto } drawn ? Styles.DrawStyles.Of(drawn.DrawStyle).Families : [];
        var planScene = new PlanScene(scene.Number, scene.DurationSeconds, scene.Visual, PlanTiming.Sentences(scene.Caption, request.Spans),
            scene.Image.Length == 0 ? string.Empty : request.ImagePrompt.Length > 0 ? request.ImagePrompt : scene.Visual);
        var user = QualityPrompt.RepairUser(PlanPromptBuilder.User(input.Title, input.Language, style, [planScene]), scene.Number, scene.Plan, request.Findings);
        var answer = await AskAsync(request.Call, PipelineSteps.QualityRepairCost, PlanPromptBuilder.System(motion, tempo, family, false,
            References.ReferenceTempoMapper.Target(style)), user,
            PlanPromptBuilder.Schema(), QualityLimits.RepairOutputTokens, [], cancellationToken).ConfigureAwait(false);
        var items = PlanReader.Read(answer);
        var item = items.FirstOrDefault(i => i.Scene == scene.Number) ?? (items.Count == 1 ? items[0] : null);
        if (item is null)
        {
            logger.LogWarning("The repaired plan of scene {Scene} could not be read; the scene keeps its plan", scene.Number);
            return null;
        }

        item.Scene = scene.Number;
        return item;
    }

    private IReadOnlyList<QualityFinding>? Findings(string what, string answer, int scenes)
    {
        var findings = QualityPrompt.ReadFindings(answer, scenes);
        if (findings is null)
        {
            logger.LogWarning("The {What} answer could not be read; it is skipped", what);
        }

        return findings;
    }

    /// <summary>One call: the files are attached as pictures; its price is spent under <paramref name="step"/> (a cut answer's charge too).</summary>
    private async Task<string> AskAsync(QualityCall call, string step, string system, string user, JsonObject schema, int maxOutput,
        IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        var images = new List<ImagePart>();
        foreach (var file in files)
        {
            var mime = Path.GetExtension(file).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png";
            images.Add(new ImagePart(await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false), mime));
        }

        var model = call.Model;
        var chat = chats.Create(model.Model, model.Key.Secret);
        try
        {
            var answer = await chat.SendAsync(new ChatRequest(system, user, CacheSystem: images.Count == 0, Schema: schema, MaxOutputTokens: maxOutput,
                Effort: model.Effort, Images: images.Count > 0 ? images : null), cancellationToken).ConfigureAwait(false);
            Spend(call, step, costs.Exact(model.Model, answer.Usage, answer.ReportedCostUsd));
            return answer.Text;
        }
        catch (Providers.ProviderException ex) when (ex.Code is "truncated" or "incomplete")
        {
            logger.LogWarning("A quality answer was cut off ({Code}); what came is read", ex.Code);
            if (ex.ChargedUsage is { } charged)
            {
                Spend(call, step, costs.Exact(model.Model, charged));
            }

            return ex.PartialText;
        }
        catch (Providers.ProviderException ex) when (ex.ChargedUsage is { } charged)
        {
            Spend(call, step, costs.Exact(model.Model, charged));
            throw;
        }
    }

    private static void Spend(QualityCall call, string step, decimal usd) =>
        call.Steps.Spend(new VideoCost { VideoId = call.VideoId, Step = step, Usd = usd, Exact = true });
}
