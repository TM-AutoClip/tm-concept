using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;

namespace TMConcept.Core.Concepts;

/// <summary>
/// AI edit of one concept (10 Nov 2026, the developer: "I press Edit, give an extra prompt, the story goes together with my prompt and the
/// model edits it"): ONE chat call with the profile's concept-step model and effort (resolved like concept generation) and the prompt of
/// <see cref="ConceptPromptBuilder.BuildEdit"/> (the channel, the current title, summary and content with the user's placeholders, the
/// user's wish), streamed and cancelable. The answer has the format of concept generation, so <see cref="ConceptResponseParser"/> reads
/// it; the first complete concept is the result. Nothing is saved here: the page shows the result in the editor and the user saves it.
/// The call is a run of its own (<see cref="RunKind.ConceptEdit"/>), so the month's spending counts it, also when it fails after being
/// billed. A cut-off answer (max_tokens) or one without a readable concept changes nothing: a half concept is no edit.
/// </summary>
public sealed class ConceptEditor(IProfileStore profiles, IRunStore runs, IChatClientFactory chats, ICostCalculator costs, TimeProvider clock,
    ILogger<ConceptEditor> logger)
{
    /// <summary>The estimate of one edit of <paramref name="concept"/> (its content read) with <paramref name="model"/> at <paramref name="effort"/>.</summary>
    public async Task<CostEstimate> EstimateAsync(Profile profile, Concept concept, string instruction, CatalogModel model, string effort,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var prompt = await PromptAsync(profile, concept, instruction, cancellationToken).ConfigureAwait(false);
        var (low, high) = OutputRange(profile, concept, model, effort);
        return costs.EstimateChat(model, costs.Tokens(prompt.System + prompt.User, profile.ContentLanguage), 0, low, high);
    }

    /// <summary>Asks for the edited concept; <paramref name="progress"/> gets the call's steps (sent, thinking, writing, done).</summary>
    public async Task<ConceptEditResult> EditAsync(Profile profile, Concept concept, string instruction, ResolvedModel model, IProgress<ChatProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(concept);
        ArgumentNullException.ThrowIfNull(model);
        var prompt = await PromptAsync(profile, concept, instruction, cancellationToken).ConfigureAwait(false);
        var (low, high) = OutputRange(profile, concept, model.Model, model.Effort);
        var estimate = costs.EstimateChat(model.Model, costs.Tokens(prompt.System + prompt.User, profile.ContentLanguage), 0, low, high);
        var run = new Run
        {
            ProfileId = profile.Id,
            Kind = RunKind.ConceptEdit,
            Provider = model.Model.Provider,
            Model = model.Model.Id,
            KeyLabel = model.Key.Label,
            Requested = 1,
            ExtraPrompt = instruction.Trim(),
            EstimateMinUsd = estimate.MinUsd,
            EstimateMaxUsd = estimate.MaxUsd,
            StartedAtUtc = clock.GetUtcNow(),
        };
        var maxTokens = ConceptOutputBudget.MaxOutputTokens(model.Model);
        logger.LogInformation("AI edit of concept #{Number} with {Provider}/{Model}, max_tokens {MaxTokens}, effort {Effort}", concept.Number,
            model.Model.Provider, model.Model.Id, maxTokens, model.Effort.Length > 0 ? model.Effort : "automatic");
        ChatResponse answer;
        try
        {
            answer = await chats.Create(model.Model, model.Key.Secret).SendAsync(new ChatRequest(prompt.System, prompt.User, Schema: prompt.Schema,
                MaxOutputTokens: maxTokens, Effort: model.Effort, Stream: true, Progress: progress), cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException ex)
        {
            // A cut-off or refused answer may still be billed: kept as a failed run; the concept stays as it was.
            run.ActualUsd = ex.ChargedUsage is { } charged ? costs.Exact(model.Model, charged) : 0;
            run.InputTokens = ex.ChargedUsage?.Input ?? 0;
            run.OutputTokens = ex.ChargedUsage?.Output ?? 0;
            return await FailAsync(run, ex.Code, true).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Canceled while the call ran: the provider may bill the prompt (and what was written so far, which is not known here). The
            // prompt is kept as an estimated cost, so the month's spending does not miss the call.
            var input = costs.Tokens(prompt.System + prompt.User, profile.ContentLanguage);
            run.InputTokens = input;
            run.ActualUsd = costs.Exact(model.Model, new ChatUsage(input, 0, 0, 0));
            return await FailAsync(run, "canceled", false).ConfigureAwait(false);
        }

        run.ActualUsd = costs.Exact(model.Model, answer.Usage, answer.ReportedCostUsd);
        run.InputTokens = answer.Usage.Input;
        run.CachedTokens = answer.Usage.CacheRead;
        run.CacheWriteTokens = answer.Usage.CacheWrite;
        run.OutputTokens = answer.Usage.Output;
        var parsed = ConceptResponseParser.Parse(answer.Text);
        foreach (var problem in parsed.Problems)
        {
            logger.LogWarning("AI edit of concept #{Number}: {Problem}", concept.Number, problem);
        }

        if (parsed.Truncated || parsed.Concepts.Count == 0)
        {
            return await FailAsync(run, parsed.Truncated ? "incomplete" : "parse", true).ConfigureAwait(false);
        }

        run.Accepted = 1;
        run.Status = RunStatus.Succeeded;
        run.CostExact = true;
        run.FinishedAtUtc = clock.GetUtcNow();
        await KeepAsync(run).ConfigureAwait(false);
        logger.LogInformation("AI edit of concept #{Number}: done with {Provider}/{Model}, {Output} output tokens, {Usd} USD", concept.Number,
            model.Model.Provider, model.Model.Id, run.OutputTokens, run.ActualUsd);
        return new ConceptEditResult(parsed.Concepts[0], run.ActualUsd, true, string.Empty);
    }

    private async Task<ConceptEditResult> FailAsync(Run run, string code, bool exact)
    {
        run.CostExact = exact;
        run.Status = code == "canceled" ? RunStatus.Canceled : RunStatus.Failed;
        run.Error = code;
        run.FinishedAtUtc = clock.GetUtcNow();
        await KeepAsync(run).ConfigureAwait(false);
        logger.LogWarning("AI edit failed ({Code}) after {Usd} USD{Estimated}; the concept is unchanged", code, run.ActualUsd, exact ? string.Empty : " (estimated)");
        return new ConceptEditResult(null, run.ActualUsd, exact, code);
    }

    /// <summary>
    /// Output tokens of one edit, low and high end (thinking included): the rewritten concept is about as long as the current one or as the
    /// target length asks, whichever is longer, times the thinking share of the effort; never more than max_tokens.
    /// </summary>
    private (long Low, long High) OutputRange(Profile profile, Concept concept, CatalogModel model, string effort)
    {
        var visible = Math.Max(ConceptOutputBudget.TokensPerConcept(profile.ContentLanguage, profile.TargetSeconds),
            costs.Tokens(concept.Title + concept.Summary + concept.Content, profile.ContentLanguage));
        var (low, high) = ConceptOutputBudget.ThinkingShare(model, effort ?? string.Empty);
        var roof = ConceptOutputBudget.MaxOutputTokens(model);
        return ((long)Math.Min(visible * low, roof), (long)Math.Min(visible * high, roof));
    }

    private async Task<ConceptPrompt> PromptAsync(Profile profile, Concept concept, string instruction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(concept);
        var images = await profiles.ImagesAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        return ConceptPromptBuilder.BuildEdit(new ConceptEditPromptInput(profile, images, concept.Title, concept.Summary, concept.Content, instruction ?? string.Empty));
    }

    private async Task KeepAsync(Run run)
    {
        try
        {
            await runs.AddAsync(run, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The result is shown anyway; only the month's spending would miss this call.
            logger.LogError(ex, "The AI edit run could not be kept");
        }
    }
}
