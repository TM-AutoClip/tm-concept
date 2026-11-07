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
/// request is sent once more without them (the parser tolerates a loose answer). No sampling parameters. <c>finish_reason: length</c> (with
/// the partial text), a refusal, an <c>error</c> object or <c>finish_reason: error</c> inside a 200 answer, an answer without choices and a
/// stream that ended without a finish reason are errors with the billed usage.
/// </summary>
public sealed class OpenAICompatibleChatClient(OpenAICompatibleProviderBase provider, ProviderHttp http, CatalogModel model, string apiKey, ILogger logger)
    : IChatClient
{
    // Fields a provider may not know: after a 400/422 the request goes once more without the ones it had.
    private static readonly string[] Optional = ["response_format", "stream_options"];

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
        JsonNode? node;
        string requestId;
        bool streamed;
        try
        {
            (node, requestId, streamed) = await AnswerAsync(body, request.Stream, cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException ex) when (ex.Status is 400 or 422 && Optional.Any(body.ContainsKey))
        {
            var rejected = Optional.Where(body.ContainsKey).ToList();
            logger.LogWarning("{Provider} {Model}: {Fields} was rejected; sending once more without it", provider.Name, Model.Id, string.Join(", ", rejected));
            foreach (var field in rejected)
            {
                body.Remove(field);
            }

            (node, requestId, streamed) = await AnswerAsync(body, request.Stream, cancellationToken).ConfigureAwait(false);
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
            throw new ProviderException($"{provider.Name}: the model refused.") { Code = "refusal", ChargedUsage = usage, RequestId = requestId };
        }

        return finish == "length"
            ? throw new ProviderException($"{provider.Name}: the answer was cut off.")
            {
                Code = "truncated",
                ChargedUsage = usage,
                RequestId = requestId,
                PartialText = text,
            }
            : new ChatResponse(text, usage, reported, finish, requestId);
    }

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
    private async Task<(JsonNode? Node, string RequestId, bool Streamed)> AnswerAsync(JsonObject body, bool stream, CancellationToken cancellationToken)
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

        if (!stream)
        {
            var reply = await http.SendAsync(Create, provider.Name, apiKey, cancellationToken).ConfigureAwait(false);
            return (JsonNode.Parse(reply.Text), reply.RequestId, false);
        }

        var reader = new OpenAICompatibleStreamReader();
        var streamed = await http.StreamAsync(Create, provider.Name, apiKey, reader.Add, cancellationToken).ConfigureAwait(false);
        if (reader.Skipped > 0)
        {
            logger.LogWarning("{Provider} {Model}: {Count} stream chunk(s) could not be read and were skipped (request {RequestId})", provider.Name, Model.Id,
                reader.Skipped, streamed.RequestId.Length > 0 ? streamed.RequestId : "-");
        }

        return (reader.Answer(), streamed.RequestId, reader.Streamed);
    }
}
