using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;
using TMConcept.Core.Text;

namespace TMConcept.Core.Documents;

/// <summary>
/// "Find topics" of the documents section: ONE call to the topics step's model with the documents prompt
/// (<see cref="ConceptPromptBuilder.BuildDocumentTopics"/>): the passages that fit the model's context (picked for the subject, or spread
/// over the documents without one), the channel content, the profile's covered concepts, disliked topics and rated videos; the answer is
/// the asked number of topics in the interface language, each with the documents that cover it and search words in their language. Like
/// "Suggest topics", a topic equal (folded, see <see cref="TurkishFold"/>) to a concept title of the profile or to a picked suggestion is
/// dropped and counted. The call is a run of its own (<see cref="RunKind.DocumentTopics"/>), so the month's spending counts it, also when it
/// fails after being billed. An unreadable answer is asked again (at most twice).
/// </summary>
public sealed class DocumentTopicFinder(DocumentGrounder grounder, IPromptMemorySource memory, IConceptStore concepts, ITopicSuggestionStore suggestions,
    IRunStore runs, IChatClientFactory chats, ICostCalculator costs, TimeProvider clock, ILogger<DocumentTopicFinder> logger)
{
    /// <summary>The concept statuses whose titles count as made: all of them (a trashed or produced concept is no new topic either).</summary>
    private static readonly ConceptStatus[] AllStatuses = Enum.GetValues<ConceptStatus>();

    /// <summary>The asked number of topics kept within <see cref="DocumentLimits.TopicCountMin"/>–<see cref="DocumentLimits.TopicCountMax"/>.</summary>
    public static int Clamp(int count) => Math.Clamp(count, DocumentLimits.TopicCountMin, DocumentLimits.TopicCountMax);

    /// <summary>Output tokens of a usual answer of <paramref name="count"/> topics (the high end of the estimate: thinking, then about 60 per topic).</summary>
    public static int OutputFor(int count) => 1_000 + (60 * Clamp(count));

    /// <summary>The answer limit of the call (the model's own limit when it is smaller).</summary>
    public static int MaxOutput(CatalogModel model, int count)
    {
        ArgumentNullException.ThrowIfNull(model);
        var roof = Math.Max(4_000, OutputFor(count) * 3);
        return model.MaxOutputTokens > 0 ? Math.Min(roof, model.MaxOutputTokens) : roof;
    }

    /// <summary>The estimate of one press (no network, no model of this computer).</summary>
    public async Task<CostEstimate> EstimateAsync(Profile profile, CatalogModel model, string subject, IReadOnlyList<Guid> documents, int count, string language,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(model);
        count = Clamp(count);
        var (prompt, _, maxOutput, passages) = await PromptAsync(profile, model, subject, documents, count, language, true, cancellationToken).ConfigureAwait(false);
        return costs.EstimateChat(model, costs.Tokens(prompt.System + prompt.User, language) + passages, 0, Math.Min(12 * count, maxOutput), Math.Min(OutputFor(count), maxOutput));
    }

    /// <summary>Finds <paramref name="count"/> topics; <paramref name="progress"/> gets the call's steps.</summary>
    public async Task<DocumentTopics> FindAsync(Profile profile, ResolvedModel model, string subject, IReadOnlyList<Guid> documents, int count, string language,
        IProgress<ChatProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(model);
        count = Clamp(count);
        var (prompt, grounding, maxOutput, _) = await PromptAsync(profile, model.Model, subject, documents, count, language, false, cancellationToken).ConfigureAwait(false);
        var run = new Run
        {
            ProfileId = profile.Id,
            Kind = RunKind.DocumentTopics,
            Provider = model.Model.Provider,
            Model = model.Model.Id,
            KeyLabel = model.Key.Label,
            Requested = count,
            ExtraPrompt = subject.Trim(),
            CostExact = true,
            StartedAtUtc = clock.GetUtcNow(),
        };
        await runs.AddAsync(run, CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Document topics {Run}: {Count} topics from {Documents} documents with {Provider}/{Model}, prompt version {Version}", run.Id, count,
            grounding.Documents.Count, model.Model.Provider, model.Model.Id, ConceptPromptBuilder.DocumentPromptVersion(language));
        var cut = false;
        try
        {
            var client = chats.Create(model.Model, model.Key.Secret);
            var topics = await JsonAnswerRetry.RunAsync<IReadOnlyList<DocumentTopic>>($"Document topics {run.Id}", async (_, token) =>
            {
                string text;
                try
                {
                    var answer = await client.SendAsync(new ChatRequest(prompt.System, prompt.User, Schema: prompt.Schema, MaxOutputTokens: maxOutput,
                        Effort: TopicSuggester.EffortFor(model.Model), Stream: true, Progress: progress), token).ConfigureAwait(false);
                    Charge(run, model.Model, answer.Usage, answer.ReportedCostUsd);
                    text = answer.Text;
                }
                catch (ProviderException ex) when (ex.ChargedUsage is { } charged)
                {
                    Charge(run, model.Model, charged, null);
                    if (ex.Code is not ("truncated" or "incomplete") || ex.PartialText.Length == 0)
                    {
                        throw;
                    }

                    cut = true;
                    text = ex.PartialText;
                }

                return DocumentTopicParser.Parse(text, grounding, count);
            }, logger, cancellationToken).ConfigureAwait(false);
            var earlier = await EarlierAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            var fresh = topics.Where(t => !earlier.Contains(TurkishFold.Fold(t.Text))).ToList();
            run.Accepted = fresh.Count;
            run.Status = RunStatus.Succeeded;
            logger.LogInformation("Document topics {Run}: {Count} topics ({Repeats} already made or picked, dropped) for {Usd} USD{Cut}", run.Id, fresh.Count,
                topics.Count - fresh.Count, run.ActualUsd, cut ? " (cut off)" : string.Empty);
            return new DocumentTopics(fresh, run.ActualUsd, cut);
        }
        catch (ProviderException ex)
        {
            run.Status = RunStatus.Failed;
            run.Error = ex.Code;
            logger.LogWarning("Document topics {Run} failed ({Code}) after {Usd} USD: {Message}", run.Id, ex.Code, run.ActualUsd, ex.Message);
            throw;
        }
        catch (OperationCanceledException)
        {
            // The prompt may be billed: counted as an estimate, so the month's spending does not miss the call.
            var input = costs.Tokens(prompt.System + prompt.User, language);
            run.InputTokens += input;
            run.ActualUsd += costs.Exact(model.Model, new ChatUsage(input, 0, 0, 0));
            run.CostExact = false;
            run.Status = RunStatus.Canceled;
            run.Error = "canceled";
            throw;
        }
        finally
        {
            run.FinishedAtUtc = clock.GetUtcNow();
            await runs.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<(ConceptPrompt Prompt, DocumentGrounding Grounding, int MaxOutput, long EstimatedPassages)> PromptAsync(Profile profile, CatalogModel model, string subject,
        IReadOnlyList<Guid> documents, int count, string language, bool estimate, CancellationToken cancellationToken)
    {
        var past = await memory.ReadAsync(profile, cancellationToken).ConfigureAwait(false);
        var input = new DocumentTopicPromptInput(profile, past, subject ?? string.Empty, DocumentGrounding.Empty, count, language);
        var bare = ConceptPromptBuilder.BuildDocumentTopics(input);
        var promptTokens = costs.Tokens(bare.System + bare.User, language);
        var maxOutput = MaxOutput(model, count);
        var lowered = DocumentBudget.LoweredOutput(model, promptTokens, OutputFor(count), maxOutput);
        var budget = DocumentBudget.PassageTokens(model, promptTokens, lowered ?? maxOutput);
        if (budget == 0)
        {
            throw new ProviderException("The model's context leaves no room for document passages.") { Code = "context" };
        }

        if (estimate)
        {
            // The price line reads no passage: the budget (or the whole of shorter documents) is charged as extra prompt tokens.
            var passages = await grounder.EstimateTokensAsync(profile, documents, budget, cancellationToken).ConfigureAwait(false);
            return (bare, DocumentGrounding.Empty, lowered ?? maxOutput, passages);
        }

        // Passages are counted for the documents' script, not the interface language (a Japanese article is not English text).
        var grounding = await grounder.GroundAsync(profile, documents, subject ?? string.Empty, budget, false, PassageTokens.For(costs, profile.ContentLanguage),
            cancellationToken).ConfigureAwait(false);
        return (ConceptPromptBuilder.BuildDocumentTopics(input with { Grounding = grounding }), grounding, lowered ?? maxOutput, 0);
    }

    /// <summary>The folded concept titles of the profile (every status) and its picked suggestions: topics already made.</summary>
    private async Task<HashSet<string>> EarlierAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var all = await concepts.ListAsync(profileId, AllStatuses, false, cancellationToken).ConfigureAwait(false);
        var picked = await suggestions.ListAsync(profileId, int.MaxValue, cancellationToken).ConfigureAwait(false);
        return all.Select(c => TurkishFold.Fold(c.Title))
            .Concat(picked.Where(s => s.Outcome == TopicOutcome.Picked).Select(s => TurkishFold.Fold(s.Text)))
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    private void Charge(Run run, CatalogModel model, ChatUsage usage, decimal? reported)
    {
        run.InputTokens += usage.Input;
        run.CachedTokens += usage.CacheRead;
        run.CacheWriteTokens += usage.CacheWrite;
        run.OutputTokens += usage.Output;
        run.ActualUsd += costs.Exact(model, usage, reported);
    }
}
