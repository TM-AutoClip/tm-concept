using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;
using TMConcept.Core.Text;

namespace TMConcept.Core.Channels;

/// <summary>
/// <see cref="IChannelStyleWriter"/>: ONE call (no retry, no second call) with <see cref="ChannelPromptBuilder"/>'s prompt, effort low like the
/// topic suggestions (<see cref="TopicSuggester.EffortFor"/>), streamed and cancelable. A cut answer keeps its complete content when there is
/// some. The call is kept as a run (<see cref="RunKind.ChannelInspiration"/>, no profile yet) whatever happens — succeeded, failed or canceled
/// (the prompt counted as an estimate then) — so the month's spending counts it. Only the numbers and the outcome are logged, never the
/// channel's texts or the answer.
/// </summary>
public sealed class ChannelStyleWriter(IChatClientFactory chats, ICostCalculator costs, IRunStore runs, TimeProvider clock, ILogger<ChannelStyleWriter> logger)
    : IChannelStyleWriter
{
    /// <summary>max_tokens for <paramref name="model"/>: <see cref="ChannelLimits.OutputMax"/>, or the model's limit when it is smaller.</summary>
    public static int OutputFor(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.MaxOutputTokens > 0 ? Math.Min(ChannelLimits.OutputMax, model.MaxOutputTokens) : ChannelLimits.OutputMax;
    }

    /// <summary>The largest listing a look can give (the estimate's prompt): every title, description and number at its limit.</summary>
    public static ChannelInspiration Largest()
    {
        var videos = Enumerable.Range(0, ChannelLimits.ListCount)
            .Select(i => new ChannelVideo(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "https://example.com/" + i, new string('w', ChannelLimits.TitleMax),
                600, new string('w', ChannelLimits.DescriptionMax), null, 0, 0, false))
            .ToList();
        var listing = new ChannelListing(new string('w', ChannelLimits.NameMax), new string('w', ChannelLimits.ChannelDescriptionMax), null, videos, []);
        return new ChannelInspiration(listing, new ChannelFacts(60, VideoFormat.Vertical, ContentLanguages.English),
            new References.ReferenceTempoProfile { CutsPer10Seconds = 4, CameraShare = 0.5, Bpm = 120 }, 1, 0, null, string.Empty);
    }

    /// <inheritdoc />
    public CostEstimate Estimate(CatalogModel model, string language)
    {
        ArgumentNullException.ThrowIfNull(model);
        var prompt = ChannelPromptBuilder.Build(Largest(), language);
        var roof = OutputFor(model);
        return costs.EstimateChat(model, costs.Tokens(prompt.System + prompt.User, language), 0, Math.Min(ChannelLimits.OutputMin, roof),
            Math.Min(ChannelLimits.OutputExpected, roof));
    }

    /// <inheritdoc />
    public async Task<ChannelStyleResult> WriteAsync(ChannelInspiration inspiration, ResolvedModel model, string language, IProgress<ChatProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inspiration);
        ArgumentNullException.ThrowIfNull(model);
        var prompt = ChannelPromptBuilder.Build(inspiration, language);
        var run = new Run
        {
            Kind = RunKind.ChannelInspiration,
            Provider = model.Model.Provider,
            Model = model.Model.Id,
            KeyLabel = model.Key.Label,
            Requested = 1,
            StartedAtUtc = clock.GetUtcNow(),
        };
        var estimate = Estimate(model.Model, language);
        run.EstimateMinUsd = estimate.MinUsd;
        run.EstimateMaxUsd = estimate.MaxUsd;
        string answer;
        ChatUsage usage;
        decimal? reported = null;
        try
        {
            var client = chats.Create(model.Model, model.Key.Secret);
            try
            {
                var response = await client.SendAsync(new ChatRequest(prompt.System, prompt.User, Schema: ChannelPromptBuilder.Schema(), MaxOutputTokens: OutputFor(model.Model),
                    Effort: TopicSuggester.EffortFor(model.Model), Stream: true, Progress: progress), cancellationToken).ConfigureAwait(false);
                answer = response.Text;
                usage = response.Usage;
                reported = response.ReportedCostUsd;
            }
            catch (ProviderException ex) when (ex.Code is "truncated" or "incomplete" && ex.ChargedUsage is { } charged && Content(ex.PartialText).Length > 0)
            {
                // Cut off after the content was written: it was paid for, so it is kept.
                logger.LogWarning("The channel content answer was cut off ({Code}); the content written before the cut is kept", ex.Code);
                answer = ex.PartialText;
                usage = charged;
            }
        }
        catch (ProviderException ex)
        {
            var charged = ex.ChargedUsage ?? ChatUsage.Zero;
            await FinishAsync(run, RunStatus.Failed, ex.Code, costs.Exact(model.Model, charged), exact: true, charged).ConfigureAwait(false);
            logger.LogWarning("The channel content call failed ({Code}, request {RequestId}) after {Usd} USD", ex.Code, ex.RequestId.Length > 0 ? ex.RequestId : "-",
                run.ActualUsd);
            throw;
        }
        catch (OperationCanceledException)
        {
            // The provider may bill the prompt: it is kept as an estimated cost, so the month's spending does not miss the call.
            var tokens = costs.Tokens(prompt.System + prompt.User, language);
            var promptUsage = new ChatUsage(tokens, 0, 0, 0);
            await FinishAsync(run, RunStatus.Canceled, "canceled", costs.Exact(model.Model, promptUsage), exact: false, promptUsage).ConfigureAwait(false);
            logger.LogInformation("The channel content call was canceled; about {Usd} USD counted for the prompt", run.ActualUsd);
            throw;
        }

        var cost = costs.Exact(model.Model, usage, reported);
        var content = Content(answer);
        if (content.Length == 0)
        {
            await FinishAsync(run, RunStatus.Failed, "parse", cost, exact: true, usage).ConfigureAwait(false);
            logger.LogWarning("The channel content answer had no content ({Output} output tokens, {Usd} USD)", usage.Output, cost);
            throw new ChannelInspireException("The model's answer had no channel content.") { Code = "answer" };
        }

        run.Accepted = 1;
        await FinishAsync(run, RunStatus.Succeeded, string.Empty, cost, exact: true, usage).ConfigureAwait(false);
        logger.LogInformation("Channel content written by {Provider}/{Model}: {Characters} characters, {Input} input and {Output} output tokens, {Usd} USD",
            model.Model.Provider, model.Model.Id, content.Length, usage.Input, usage.Output, cost);
        return new ChannelStyleResult(content, cost, Exact: true);
    }

    /// <summary>The channel content of an answer (<c>{"concept_text": "…"}</c>, tolerantly read), cleaned and cut to the profile's limit; empty when none.</summary>
    public static string Content(string? answer)
    {
        var node = ModelJson.TryParseObject(answer);
        var text = node?[ChannelPromptBuilder.AnswerKey] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
        return TextClean.Clean(text ?? Partial(answer), ProfileLimits.ConceptTextMax);
    }

    /// <summary>The string after <c>"concept_text":</c> as far as it arrived (a cut answer has no closing quote); null when there is none.</summary>
    private static string? Partial(string? answer)
    {
        var text = ModelJson.StripThinking(answer);
        var key = text.IndexOf('"' + ChannelPromptBuilder.AnswerKey + '"', StringComparison.Ordinal);
        var colon = key < 0 ? -1 : text.IndexOf(':', key);
        var open = colon < 0 ? -1 : text.IndexOf('"', colon);
        if (open < 0)
        {
            return null;
        }

        var end = open + 1;
        while (end < text.Length && text[end] != '"')
        {
            end += text[end] == '\\' ? 2 : 1;
        }

        var raw = text[(open + 1)..Math.Min(end, text.Length)];
        if (raw.EndsWith('\\') && raw.Reverse().TakeWhile(c => c == '\\').Count() % 2 == 1)
        {
            raw = raw[..^1];
        }

        try
        {
            return JsonNode.Parse('"' + raw + '"')?.GetValue<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task FinishAsync(Run run, RunStatus status, string error, decimal usd, bool exact, ChatUsage usage)
    {
        run.Status = status;
        run.Error = error;
        run.ActualUsd = usd;
        run.CostExact = exact;
        run.InputTokens = usage.Input;
        run.CachedTokens = usage.CacheRead;
        run.CacheWriteTokens = usage.CacheWrite;
        run.OutputTokens = usage.Output;
        run.FinishedAtUtc = clock.GetUtcNow();
        try
        {
            await runs.AddAsync(run, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The draft is shown anyway; only the month's spending would miss this call.
            logger.LogError(ex, "The channel content run could not be kept");
        }
    }
}
