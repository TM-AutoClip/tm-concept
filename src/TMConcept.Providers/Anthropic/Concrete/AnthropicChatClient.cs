using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Anthropic;

/// <summary>
/// Claude through <c>POST v1/messages</c>, streamed (a long answer must not hit a timeout). The stable system text carries a cache marker
/// when asked; a schema goes in <c>output_config.format</c> and an effort level in <c>output_config.effort</c>. No sampling parameters and no
/// thinking settings are sent (the model's defaults apply). The stop reason is checked before the text: <c>refusal</c>, <c>max_tokens</c>,
/// a context overflow and a stream that never finished are errors (with the tokens already billed, the raw stop reason and, for a cut, the
/// text that arrived). Every answer writes one log line: model, raw stop reason, the max_tokens and effort sent, the tokens, the request id
/// and the time (a warning when it ended badly; never the prompt or the key). The request's progress gets sent → thinking (lines but no text
/// yet: thinking blocks, pings) → writing → done (<see cref="ChatProgressReporter"/>).
/// </summary>
public sealed class AnthropicChatClient(AnthropicProvider provider, ProviderHttp http, CatalogModel model, string apiKey, ILogger logger) : IChatClient
{
    private static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/gif", "image/webp" };

    /// <inheritdoc />
    public CatalogModel Model { get; } = model;

    /// <summary>The request body (pure; tests read it).</summary>
    public JsonObject Body(ChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var system = new JsonObject { ["type"] = "text", ["text"] = request.System };
        if (request.CacheSystem)
        {
            system["cache_control"] = new JsonObject { ["type"] = "ephemeral" };
        }

        var content = new JsonArray();
        foreach (var image in (request.Images ?? []).Where(i => ImageTypes.Contains(i.MimeType)))
        {
            content.Add((JsonNode)new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.MimeType, ["data"] = Convert.ToBase64String(image.Data) },
            });
        }

        content.Add((JsonNode)new JsonObject { ["type"] = "text", ["text"] = request.User });
        var body = new JsonObject
        {
            ["model"] = Model.Id,
            ["max_tokens"] = MaxTokens(request),
            ["system"] = new JsonArray(system),
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
            ["stream"] = true,
        };
        var output = new JsonObject();
        if (request.Schema is not null)
        {
            output["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = request.Schema.DeepClone() };
        }

        if (request.Effort.Length > 0 && Model.Efforts.Contains(request.Effort))
        {
            output["effort"] = request.Effort;
        }

        if (output.Count > 0)
        {
            body["output_config"] = output;
        }

        return body;
    }

    /// <inheritdoc />
    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var body = Body(request);
        var json = body.ToJsonString();
        var watch = Stopwatch.StartNew();

        // Read while it streams: a long answer (a video code of 64 000 tokens takes about 11 minutes) has no single time limit then.
        var reader = new AnthropicStreamReader();
        var progress = new ChatProgressReporter(request.Progress, TimeProvider.System);
        progress.Sent();
        StreamReply reply;
        try
        {
            reply = await http.StreamAsync(() =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "messages"))
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                provider.Authorize(message, apiKey);
                return message;
            }, provider.Name, apiKey, line =>
            {
                reader.Add(line);
                progress.Line(reader.TextLength, () => reader.Text);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException ex) when (ex.Code is "timeout" or "network" && reader.Answer() is { Streamed: true } partial
            && (partial.Usage.Prompt > 0 || partial.Usage.Output > 0))
        {
            // The stream broke after it started: what arrived until then was billed, so the failure carries it.
            throw new ProviderException(ex.Message, ex) { Code = ex.Code, RequestId = ex.RequestId, ChargedUsage = partial.Usage, PartialText = partial.Text };
        }

        var answer = reader.Answer();
        progress.Done(answer.Text);
        if (answer.SkippedEvents > 0)
        {
            logger.LogWarning("{Provider} {Model}: {Count} stream event(s) could not be read and were skipped (request {RequestId})", provider.Name, Model.Id,
                answer.SkippedEvents, reply.RequestId.Length > 0 ? reply.RequestId : "-");
        }

        if (answer.Error is { } error)
        {
            throw http.Failure(provider.Name, StatusOf(error.Type), error.Raw, reply.RequestId, apiKey);
        }

        var bad = answer.StopReason is "refusal" or "max_tokens" or "model_context_window_exceeded" || (answer.StopReason.Length == 0 && answer.Streamed);
        logger.Log(bad ? LogLevel.Warning : LogLevel.Information,
            "{Provider} {Model}: stop reason {StopReason}, max_tokens {MaxTokens}, effort {Effort}, tokens in {Input} / out {Output} / cache read {CacheRead} / cache write {CacheWrite}, request {RequestId}, {Ms} ms",
            provider.Name, Model.Id, answer.StopReason.Length > 0 ? answer.StopReason : "-", MaxTokens(request),
            JsonRead.Text(body["output_config"]?["effort"]) is { Length: > 0 } effort ? effort : "automatic", answer.Usage.Input, answer.Usage.Output, answer.Usage.CacheRead,
            answer.Usage.CacheWrite, reply.RequestId.Length > 0 ? reply.RequestId : "-", watch.ElapsedMilliseconds);
        return answer.StopReason switch
        {
            "refusal" => throw Charged("refusal", answer, reply.RequestId),
            "max_tokens" or "model_context_window_exceeded" => throw Charged("truncated", answer, reply.RequestId),
            "" when answer.Streamed => throw Charged("incomplete", answer, reply.RequestId),
            _ => new ChatResponse(answer.Text, answer.Usage, null, answer.StopReason, reply.RequestId),
        };
    }

    /// <summary>The max_tokens sent: the request's, lowered to the model's limit.</summary>
    private int MaxTokens(ChatRequest request) => Math.Min(request.MaxOutputTokens, Model.MaxOutputTokens > 0 ? Model.MaxOutputTokens : request.MaxOutputTokens);

    private ProviderException Charged(string code, AnthropicAnswer answer, string requestId) =>
        new($"{provider.Name}: the answer was not complete ({code}).")
        {
            Code = code,
            ChargedUsage = answer.Usage,
            RequestId = requestId,
            PartialText = code == "refusal" ? string.Empty : answer.Text,
            StopReason = answer.StopReason,
        };

    private static int StatusOf(string type) => type switch
    {
        "overloaded_error" => 529,
        "rate_limit_error" => 429,
        "api_error" => 500,
        "authentication_error" => 401,
        "permission_error" => 403,
        "not_found_error" => 404,
        "billing_error" => 402,
        "request_too_large" => 413,
        _ => 400,
    };
}
