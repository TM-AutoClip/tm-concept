using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.OpenAICompatible;

/// <summary>
/// <c>POST chat/completions</c>. Images as <c>image_url</c> data addresses; a schema as <c>response_format</c> (json_schema, or json_object
/// on providers without it). A long call (<see cref="ChatRequest.Stream"/>) is streamed with <c>stream_options.include_usage</c> and read
/// line by line (<see cref="OpenAICompatibleStreamReader"/>). When the provider rejects one of the optional fields with 400 or 422, the
/// request is sent once more without them (the parser tolerates a loose answer). The request's output limit goes as <c>max_tokens</c>
/// (<c>max_completion_tokens</c> on OpenAI), lowered to the model's limit, and a chosen effort as <c>reasoning_effort</c> (<c>reasoning.effort</c>
/// on OpenRouter); both are optional fields too. No sampling parameters. <c>finish_reason: length</c> (with
/// the partial text), a refusal, an <c>error</c> object or <c>finish_reason: error</c> inside a 200 answer, an answer without choices and a
/// stream that ended without a finish reason are errors with the billed usage (and the raw finish reason). Every answer writes one log
/// line: model, raw finish reason, the max_tokens and effort sent ("-" when none, or after a provider refused them), the tokens, the request id and
/// the time (a warning when it ended badly; never the prompt or the key). The request's progress gets sent → thinking → writing (a streamed
/// call; reasoning chunks carry no visible text) → done (<see cref="ChatProgressReporter"/>).
/// </summary>
public sealed class OpenAICompatibleChatClient(OpenAICompatibleProviderBase provider, ProviderHttp http, CatalogModel model, string apiKey, ILogger logger)
    : IChatClient
{
    // Fields a provider may not know: after a 400/422 the request goes once more without the ones it had.
    private static readonly string[] Optional = ["response_format", "stream_options", "max_tokens", "max_completion_tokens", "reasoning_effort", "reasoning"];

    /// <inheritdoc />
    public CatalogModel Model { get; } = model;

    /// <summary>The request body (pure; tests read it).</summary>
    public JsonObject Body(ChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        JsonNode content = request.Images is { Count: > 0 } images
            ? new JsonArray(images.Select(i => (JsonNode)new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = $"data:{i.MimeType};base64,{Convert.ToBase64String(i.Data)}" },
                }).Prepend(new JsonObject { ["type"] = "text", ["text"] = request.User }).ToArray())
            : JsonValue.Create(request.User);
        var body = new JsonObject
        {
            ["model"] = Model.Id,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = content }),
            ["stream"] = request.Stream,
        };
        if (request.Stream)
        {
            // Without this OpenAI and DeepSeek send no usage in a stream (the cost would be unknown); a provider that rejects it gets the
            // request once more without it.
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        // The output limit of the request (lowered to the model's own limit): without it a provider uses its default (DeepSeek: a few
        // thousand tokens), and a long answer was cut off whatever the program asked for.
        if (request.MaxOutputTokens > 0)
        {
            body[provider.MaxTokensField] = Model.MaxOutputTokens > 0 ? Math.Min(request.MaxOutputTokens, Model.MaxOutputTokens) : request.MaxOutputTokens;
        }

        if (request.Effort.Length > 0)
        {
            provider.SetEffort(body, request.Effort);
        }

        if (request.Schema is not null)
        {
            body["response_format"] = provider.JsonSchemaFormat
                ? new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject { ["name"] = "answer", ["strict"] = true, ["schema"] = request.Schema.DeepClone() },
                }
                : new JsonObject { ["type"] = "json_object" };
        }

        provider.Extend(body);
        return body;
    }

    /// <inheritdoc />
    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = Body(request);
        var watch = Stopwatch.StartNew();
        var progress = new ChatProgressReporter(request.Progress, TimeProvider.System);
        JsonNode? node;
        string requestId;
        bool streamed;
        try
        {
            (node, requestId, streamed) = await AnswerAsync(body, request.Stream, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException ex) when (ex.Status is 400 or 422 && Optional.Any(body.ContainsKey))
        {
            var rejected = Optional.Where(body.ContainsKey).ToList();
            logger.LogWarning("{Provider} {Model}: {Fields} was rejected; sending once more without it", provider.Name, Model.Id, string.Join(", ", rejected));
            foreach (var field in rejected)
            {
                body.Remove(field);
            }

            (node, requestId, streamed) = await AnswerAsync(body, request.Stream, progress, cancellationToken).ConfigureAwait(false);
        }

        if (streamed && node?["usage"] is null)
        {
            logger.LogWarning("{Provider} {Model}: the stream reported no usage; the cost of this call is counted as 0 (request {RequestId})", provider.Name,
                Model.Id, requestId.Length > 0 ? requestId : "-");
        }

        var promptTokens = JsonRead.Long(node?["usage"]?["prompt_tokens"]);
        var cached = JsonRead.Long(node?["usage"]?["prompt_tokens_details"]?["cached_tokens"]);
        var usage = new ChatUsage(Math.Max(0, promptTokens - cached), cached, 0, JsonRead.Long(node?["usage"]?["completion_tokens"]));
        var reported = JsonRead.Decimal(node?["usage"]?["cost"]);

        // OpenRouter (and some gateways) answer HTTP 200 with only an "error" object when the upstream fails after the headers were sent,
        // or with finish_reason "error" and a partial message. Both are failures, never an empty success.
        var choice = (node?["choices"] as JsonArray) is { Count: > 0 } choices ? choices[0] : null;
        var finish = JsonRead.Text(choice?["finish_reason"]);
        progress.Done(JsonRead.Text(choice?["message"]?["content"]));
        var bad = node?["error"] is not null || choice is null || finish is "error" or "length" or "content_filter" || (streamed && finish.Length == 0);
        logger.Log(bad ? LogLevel.Warning : LogLevel.Information,
            "{Provider} {Model}: finish reason {FinishReason}, max_tokens {MaxTokens}, effort {Effort}, tokens in {Input} / out {Output} / cache read {CacheRead}, request {RequestId}, {Ms} ms",
            provider.Name, Model.Id, finish.Length > 0 ? finish : "-", MaxTokens(body), Effort(body), usage.Input, usage.Output,
            usage.CacheRead, requestId.Length > 0 ? requestId : "-", watch.ElapsedMilliseconds);
        if (node?["error"] is not null || finish == "error")
        {
            var status = ErrorStatus(node?["error"] ?? choice?["error"]);
            throw http.Failure(provider.Name, status, node?.ToJsonString() ?? string.Empty, requestId, apiKey, usage);
        }

        if (choice is null)
        {
            logger.LogWarning("{Provider} {Model}: the answer has no choices (request {RequestId})", provider.Name, Model.Id,
                requestId.Length > 0 ? requestId : "-");
            throw new ProviderException($"{provider.Name}: the answer had no choices.")
            {
                Code = "incomplete",
                ChargedUsage = usage.Prompt > 0 || usage.Output > 0 ? usage : null,
                RequestId = requestId,
            };
        }

        var text = JsonRead.Text(choice["message"]?["content"]);
        if (streamed && finish.Length == 0)
        {
            // The stream ended without a finish reason: the answer is not complete (what arrived was billed).
            throw new ProviderException($"{provider.Name}: the answer stream ended early.")
            {
                Code = "incomplete",
                ChargedUsage = usage.Prompt > 0 || usage.Output > 0 ? usage : null,
                RequestId = requestId,
                PartialText = text,
            };
        }

        if (text.Length == 0 && JsonRead.Text(choice["message"]?["refusal"]).Length > 0)
        {
            throw new ProviderException($"{provider.Name}: the model refused.") { Code = "refusal", ChargedUsage = usage, RequestId = requestId, StopReason = finish };
        }

        return finish == "length"
            ? throw new ProviderException($"{provider.Name}: the answer was cut off.")
            {
                Code = "truncated",
                ChargedUsage = usage,
                RequestId = requestId,
                PartialText = text,
                StopReason = finish,
            }
            : new ChatResponse(text, usage, reported, finish, requestId);
    }

    /// <summary>The output limit the body sends (<c>max_completion_tokens</c> or <c>max_tokens</c>), as text; "-" when none is sent.</summary>
    private static string MaxTokens(JsonObject body) => (body["max_completion_tokens"] ?? body["max_tokens"])?.ToJsonString() ?? "-";

    /// <summary>The effort the body sends (<c>reasoning_effort</c>, or <c>reasoning.effort</c> on OpenRouter); "-" when none.</summary>
    private static string Effort(JsonObject body) =>
        JsonRead.Text(body["reasoning_effort"]) is { Length: > 0 } effort ? effort : JsonRead.Text(body["reasoning"]?["effort"]) is { Length: > 0 } nested ? nested : "-";

    /// <summary>HTTP status of an error object inside a 200 body: a numeric <c>code</c> when it is one (OpenRouter), otherwise 502 (busy).</summary>
    private static int ErrorStatus(JsonNode? error)
    {
        var code = error?["code"];
        if (code is JsonValue value && value.TryGetValue<int>(out var number) && number is >= 400 and < 600)
        {
            return number;
        }

        if (code is JsonValue text && text.TryGetValue<string>(out var s) && int.TryParse(s, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed is >= 400 and < 600)
        {
            return parsed;
        }

        return 502;
    }

    /// <summary>The answer as a plain <c>chat/completions</c> body (a streamed one is put into that shape), its request id and whether it streamed.</summary>
    private async Task<(JsonNode? Node, string RequestId, bool Streamed)> AnswerAsync(JsonObject body, bool stream, ChatProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var json = body.ToJsonString();
        HttpRequestMessage Create()
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "chat/completions"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            provider.Authorize(message, apiKey);
            return message;
        }

        progress.Sent();
        if (!stream)
        {
            var reply = await http.SendAsync(Create, provider.Name, apiKey, cancellationToken).ConfigureAwait(false);
            return (JsonNode.Parse(reply.Text), reply.RequestId, false);
        }

        var reader = new OpenAICompatibleStreamReader();
        var streamed = await http.StreamAsync(Create, provider.Name, apiKey, line =>
        {
            reader.Add(line);
            progress.Line(reader.TextLength, () => reader.Text);
        }, cancellationToken).ConfigureAwait(false);
        if (reader.Skipped > 0)
        {
            logger.LogWarning("{Provider} {Model}: {Count} stream chunk(s) could not be read and were skipped (request {RequestId})", provider.Name, Model.Id,
                reader.Skipped, streamed.RequestId.Length > 0 ? streamed.RequestId : "-");
        }

        return (reader.Answer(), streamed.RequestId, reader.Streamed);
    }
}
