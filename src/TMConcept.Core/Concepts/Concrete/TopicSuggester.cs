using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;
using TMConcept.Core.Text;

namespace TMConcept.Core.Concepts;

/// <summary>
/// "Suggest topics" on the concept page: ONE call (no batching, also for 100 topics) to the cheap chat model of the profile's topics step
/// with the prompt of <see cref="ConceptPromptBuilder.BuildTopics"/> (Prompts/&lt;lang&gt;/topics.txt: the channel content and format, the
/// chosen fine-tuning, the profile's covered concepts and topic dislikes from <see cref="IPromptMemorySource"/>, the newest
/// <see cref="PromptMemoryLimits.SuggestionsSent"/> topics it suggested before: do not suggest them again); the answer is the asked number
/// (<see cref="TopicLimits"/>, 5-100) of short topics in the interface language (the user reads and ticks them on the page; each ticked
/// topic later becomes exactly one concept). The kept suggestions are only the profile's memory of what was already suggested: the tick
/// list itself lives on the page (0.1.13 removed the 0.1.12 stored "selected" list). The extra request is not sent (0.1.12): the old prompt told the model to
/// follow it even outside the channel's scope, and topics drifted away from the channel. The call is a run of its own (<see cref="RunKind.Topics"/>), so the month's spending counts it, also
/// when it fails after being billed. It is meant to be cheap and quick: effort low (none for a model without effort levels; the user's
/// effort choice is not used), streamed, cancelable, with room for thinking (0.1.7 gave it 1 500 tokens and Haiku 5.5 at effort high was
/// cut before writing a topic, 8 Nov 2026). A cut-off answer still gives the complete topics before the cut. A model may still repeat
/// itself: a topic equal (folded, see <see cref="TurkishFold"/>) to an earlier suggestion or a concept title is dropped here and counted;
/// the kept ones are saved as the profile's suggestions (10 Nov 2026, the developer: "could it ask the same ones again every time?").
/// </summary>
public sealed class TopicSuggester(IConceptStore concepts, ITopicSuggestionStore suggestions, IPromptMemorySource memory, IRunStore runs, IChatClientFactory chats, ICostCalculator costs, TimeProvider clock,
    ILogger<TopicSuggester> logger)
{
    /// <summary>The concept statuses whose titles are sent: all of them (a trashed or produced concept is no new topic either).</summary>
    private static readonly ConceptStatus[] AllStatuses = Enum.GetValues<ConceptStatus>();

    /// <summary>
    /// max_tokens of the call (thinking included), lowered to the model's limit when that is smaller. Only what is produced is paid for, so
    /// this is a roof, not what the call costs.
    /// </summary>
    public const int OutputMax = 16_000;

    /// <summary>Fewest output tokens of an answer of <paramref name="count"/> topics (about 12 tokens for a short English topic).</summary>
    public static int OutputMinFor(int count) => 12 * Clamp(count);

    /// <summary>
    /// Output tokens of a usual answer of <paramref name="count"/> topics at effort low (the high end of the estimate): about 1 000 of thinking
    /// and the scope line, then up to 50 per topic (a Turkish topic of 90 characters is 35-45 tokens). 10 topics = 1 500, 100 = 6 000: still
    /// well under <see cref="OutputMax"/>, so 100 topics stay one call. Lowered to <see cref="OutputFor"/> in the estimate.
    /// </summary>
    public static int OutputExpectedFor(int count) => 1_000 + (50 * Clamp(count));

    /// <summary>The asked number of topics kept within <see cref="TopicLimits"/>.</summary>
    public static int Clamp(int count) => Math.Clamp(count, TopicLimits.CountMin, TopicLimits.CountMax);

    /// <summary>The effort sent when the model has it (otherwise its lowest known level, see <see cref="EffortFor"/>).</summary>
    public const string Effort = "low";

    /// <summary>Effort levels from the cheapest up (a model's own list may hold only some of them).</summary>
    private static readonly string[] EffortOrder = ["minimal", "low", "medium", "high", "xhigh", "max"];

    /// <summary>
    /// The effort sent with <paramref name="model"/>: <see cref="Effort"/> when it has it, otherwise the lowest of its levels known to
    /// <see cref="EffortOrder"/> (a model with "minimal, medium, high" gets minimal, not the automatic high); none without effort levels.
    /// </summary>
    public static string EffortFor(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Efforts.Contains(Effort))
        {
            return Effort;
        }

        return EffortOrder.FirstOrDefault(model.Efforts.Contains) ?? string.Empty;
    }

    /// <summary>max_tokens for <paramref name="model"/>: <see cref="OutputMax"/>, or the model's limit when it is smaller.</summary>
    public static int OutputFor(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.MaxOutputTokens > 0 ? Math.Min(OutputMax, model.MaxOutputTokens) : OutputMax;
    }

    /// <summary>The estimate of one press with <paramref name="model"/>.</summary>
    public async Task<CostEstimate> EstimateAsync(Profile profile, CatalogModel model, string focus, int count, string language,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        count = Clamp(count);
        var (system, user, _) = await PromptAsync(profile, focus, count, language, cancellationToken).ConfigureAwait(false);
        var roof = OutputFor(model);
        return costs.EstimateChat(model, costs.Tokens(system + user, language), 0, Math.Min(OutputMinFor(count), roof), Math.Min(OutputExpectedFor(count), roof));
    }

    /// <summary>
    /// Asks for <paramref name="count"/> topics (kept within <see cref="TopicLimits"/>); <paramref name="progress"/> gets the call's steps
    /// (sent, thinking, writing, done). The topics come back already kept as the profile's suggestions, with their ids.
    /// </summary>
    public async Task<TopicSuggestions> SuggestAsync(Profile profile, ResolvedModel model, string focus, int count, string language,
        IProgress<ChatProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(model);
        count = Clamp(count);
        var (system, user, earlier) = await PromptAsync(profile, focus, count, language, cancellationToken).ConfigureAwait(false);
        var started = clock.GetUtcNow();
        var run = new Run
        {
            ProfileId = profile.Id,
            Kind = RunKind.Topics,
            Provider = model.Model.Provider,
            Model = model.Model.Id,
            KeyLabel = model.Key.Label,
            Requested = count,
            StartedAtUtc = started,
        };
        string text;
        ChatUsage usage;
        decimal? reported = null;
        var cut = false;
        try
        {
            var effort = EffortFor(model.Model);
            var answer = await chats.Create(model.Model, model.Key.Secret).SendAsync(new ChatRequest(system, user, Schema: ConceptPromptBuilder.TopicSchema(),
                MaxOutputTokens: OutputFor(model.Model), Effort: effort, Stream: true, Progress: progress), cancellationToken).ConfigureAwait(false);
            text = answer.Text;
            usage = answer.Usage;
            reported = answer.ReportedCostUsd;
        }
        catch (ProviderException ex) when (ex.Code is "truncated" or "incomplete" && ex.ChargedUsage is { } charged && ParsePartial(ex.PartialText, count).Count > 0)
        {
            // Cut off after some topics were written: they were paid for, so the complete ones are shown.
            logger.LogWarning("Topic suggestions were cut off ({Code}, stop reason {StopReason}, {Output} output tokens); keeping the complete topics before the cut",
                ex.Code, ex.StopReason.Length > 0 ? ex.StopReason : "-", charged.Output);
            text = ex.PartialText;
            usage = charged;
            cut = true;
        }
        catch (ProviderException ex)
        {
            // A refused or cut answer may still be billed: kept as a failed run.
            run.ActualUsd = ex.ChargedUsage is { } charged ? costs.Exact(model.Model, charged) : 0;
            run.CostExact = true;
            run.Status = RunStatus.Failed;
            run.Error = ex.Code;
            run.FinishedAtUtc = clock.GetUtcNow();
            await KeepAsync(run).ConfigureAwait(false);
            logger.LogWarning("Topic suggestions failed ({Code}, stop reason {StopReason}, request {RequestId}, {Output} output tokens) after {Usd} USD", ex.Code,
                ex.StopReason.Length > 0 ? ex.StopReason : "-", ex.RequestId.Length > 0 ? ex.RequestId : "-", ex.ChargedUsage?.Output ?? 0, run.ActualUsd);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Canceled while the call ran: the provider may bill the prompt (and what was written so far, which is not known here). The
            // prompt is kept as an estimated cost, so the month's spending does not miss the call.
            var input = costs.Tokens(system + user, language);
            run.InputTokens = input;
            run.ActualUsd = costs.Exact(model.Model, new ChatUsage(input, 0, 0, 0));
            run.CostExact = false;
            run.Status = RunStatus.Canceled;
            run.Error = "canceled";
            run.FinishedAtUtc = clock.GetUtcNow();
            await KeepAsync(run).ConfigureAwait(false);
            logger.LogInformation("Topic suggestions canceled; about {Usd} USD counted for the prompt", run.ActualUsd);
            throw new TopicsCanceledException(run.ActualUsd);
        }

        // The scope sentence only anchors the model to the channel before it lists topics; it is not shown, only logged.
        logger.LogDebug("Topic suggestions: the model's scope: {Scope}", Scope(text));
        var parsed = cut ? ParsePartial(text, count) : Parse(text, count);
        var topics = parsed.Where(t => !earlier.Contains(TurkishFold.Fold(t))).ToList();
        var repeats = parsed.Count - topics.Count;
        run.ActualUsd = costs.Exact(model.Model, usage, reported);
        run.CostExact = true;
        run.InputTokens = usage.Input;
        run.OutputTokens = usage.Output;
        run.Accepted = topics.Count;
        // A read answer whose every topic was a repeat is a successful call (only filtered here), not an unreadable one.
        run.Status = parsed.Count > 0 ? RunStatus.Succeeded : RunStatus.Failed;
        run.Error = parsed.Count > 0 ? string.Empty : "parse";
        run.FinishedAtUtc = clock.GetUtcNow();
        await KeepAsync(run).ConfigureAwait(false);
        var kept = await KeepSuggestionsAsync(profile.Id, topics, run.FinishedAtUtc.Value).ConfigureAwait(false);
        logger.LogInformation("Topic suggestions: {Count} of {Requested} topics ({Repeats} repeats dropped) from {Provider}/{Model} for {Usd} USD{Cut}", topics.Count,
            count, repeats, model.Model.Provider, model.Model.Id, run.ActualUsd, cut ? " (cut off)" : string.Empty);
        return new TopicSuggestions(kept, run.ActualUsd, run.CostExact, cut, repeats);
    }

    /// <summary>
    /// The complete topics of an answer that was cut off: every string item of the <c>"topics"</c> array whose closing quote arrived (the
    /// last, unfinished one is dropped); cleaned like <see cref="Parse"/>. A complete answer gives the same as <see cref="Parse"/>.
    /// </summary>
    public static IReadOnlyList<string> ParsePartial(string answer, int count)
    {
        var text = answer ?? string.Empty;
        var key = text.IndexOf("\"topics\"", StringComparison.Ordinal);
        var start = key < 0 ? -1 : text.IndexOf('[', key);
        if (start < 0)
        {
            return [];
        }

        var found = new List<string>();
        var at = start + 1;
        while (at < text.Length)
        {
            while (at < text.Length && (char.IsWhiteSpace(text[at]) || text[at] == ','))
            {
                at++;
            }

            if (at >= text.Length || text[at] != '"')
            {
                break;
            }

            var end = ClosingQuote(text, at + 1);
            if (end < 0)
            {
                break;
            }

            try
            {
                if (JsonNode.Parse(text[at..(end + 1)]) is JsonValue value && value.TryGetValue<string>(out var topic))
                {
                    found.Add(topic);
                }
            }
            catch (JsonException)
            {
                break;
            }

            at = end + 1;
        }

        return found.Select(t => TextClean.OneLine(t, TopicLimits.TopicMax))
            .Where(t => t.Length > 0)
            .DistinctBy(TurkishFold.Fold)
            .Take(count)
            .ToList();
    }

    /// <summary>Index of the quote that ends a JSON string starting at <paramref name="from"/> (escapes skipped); -1 when it never ends.</summary>
    private static int ClosingQuote(string text, int from)
    {
        for (var i = from; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == '"')
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The topics of an answer (<c>{"scope": "…", "topics": […]}</c>, also inside a code block; the scope is not needed here); short, one
    /// line each, no repeats, at most <paramref name="count"/> (the schema cannot carry the count: see <see cref="ConceptPromptBuilder.TopicSchema"/>).
    /// </summary>
    public static IReadOnlyList<string> Parse(string answer, int count)
    {
        var text = answer ?? string.Empty;
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return [];
        }

        try
        {
            if (JsonNode.Parse(text[start..(end + 1)])?["topics"] is not JsonArray items)
            {
                return [];
            }

            return items.Select(i => i is JsonValue value && value.TryGetValue<string>(out var topic) ? TextClean.OneLine(topic, TopicLimits.TopicMax) : string.Empty)
                .Where(t => t.Length > 0)
                .DistinctBy(TurkishFold.Fold)
                .Take(count)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task KeepAsync(Run run)
    {
        try
        {
            await runs.AddAsync(run, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The topics are shown anyway; only the month's spending would miss this call.
            logger.LogError(ex, "The topic suggestion run could not be kept");
        }
    }

    /// <summary>
    /// The scope sentence the model wrote before the topics (empty when there is none): read from the answer as far as it arrived, so a cut
    /// answer gives it too.
    /// </summary>
    private static string Scope(string answer)
    {
        var text = answer ?? string.Empty;
        var key = text.IndexOf("\"scope\"", StringComparison.Ordinal);
        var colon = key < 0 ? -1 : text.IndexOf(':', key);
        var start = colon < 0 ? -1 : text.IndexOf('"', colon);
        var end = start < 0 ? -1 : ClosingQuote(text, start + 1);
        if (end < 0)
        {
            return string.Empty;
        }

        try
        {
            return JsonNode.Parse(text[start..(end + 1)]) is JsonValue value && value.TryGetValue<string>(out var scope)
                ? TextClean.OneLine(scope, ProfileLimits.ConceptTextMax)
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Keeps the new topics as the profile's suggestions (so the next press does not bring them again) and gives them back with their ids.
    /// The topics of one press are a tick apart, so they keep the answer's order.
    /// </summary>
    private async Task<IReadOnlyList<TopicSuggestion>> KeepSuggestionsAsync(Guid profileId, IReadOnlyList<string> topics, DateTimeOffset at)
    {
        var rows = topics.Select((t, i) => new TopicSuggestion { ProfileId = profileId, Text = t, CreatedAtUtc = at.AddTicks(i) })
            .ToList();
        try
        {
            await suggestions.AddAsync(rows, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The topics are shown anyway; only the next press would not know them.
            logger.LogError(ex, "The topic suggestions could not be kept");
        }

        return rows;
    }

    /// <summary>
    /// The prompt, and the folded texts a new topic must not equal (every concept title of the profile and every earlier suggestion; the
    /// prompt sends only the newest suggestions and the condensed history).
    /// </summary>
    private async Task<(string System, string User, HashSet<string> Earlier)> PromptAsync(Profile profile, string focus, int count,
        string language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var all = await concepts.ListAsync(profile.Id, AllStatuses, false, cancellationToken).ConfigureAwait(false);
        var suggested = await suggestions.ListAsync(profile.Id, int.MaxValue, cancellationToken).ConfigureAwait(false);
        var earlier = all.Select(c => TurkishFold.Fold(c.Title)).Concat(suggested.Select(s => TurkishFold.Fold(s.Text))).Where(t => t.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var past = await memory.ReadAsync(profile, cancellationToken).ConfigureAwait(false);
        var prompt = ConceptPromptBuilder.BuildTopics(new TopicPromptInput(profile, language, focus ?? string.Empty, past,
            suggested.Take(PromptMemoryLimits.SuggestionsSent).Select(s => s.Text).ToList(), count));
        return (prompt.System, prompt.User, earlier);
    }
}
