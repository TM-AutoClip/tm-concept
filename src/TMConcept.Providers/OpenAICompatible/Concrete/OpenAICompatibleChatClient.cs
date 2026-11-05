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
/// on providers without it) — when the provider rejects the field with 400 the request is sent once more without it (the parser tolerates a
/// loose answer). No sampling parameters. <c>finish_reason: length</c> (with the partial text), a refusal, an <c>error</c> object or
/// <c>finish_reason: error</c> inside a 200 answer and an answer without choices are errors with the billed usage.
/// </summary>
public sealed class OpenAICompatibleChatClient(OpenAICompatibleProviderBase provider, ProviderHttp http, CatalogModel model, string apiKey, ILogger logger)
    : IChatClient
{
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
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = content },
            },
            ["stream"] = false,
        };
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
        var body = Body(request);
        ProviderReply reply;
        try
        {
            reply = await PostAsync(body, cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException ex) when (ex.Status == 400 && body.ContainsKey("response_format"))
        {
            logger.LogWarning("{Provider} {Model}: response_format was rejected; sending once more without it", provider.Name, Model.Id);
            body.Remove("response_format");
            reply = await PostAsync(body, cancellationToken).ConfigureAwait(false);
        }

        var node = JsonNode.Parse(reply.Text);
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
            throw http.Failure(provider.Name, status, reply.Text, reply.RequestId, apiKey, usage);
        }

        if (choice is null)
        {
            logger.LogWarning("{Provider} {Model}: the answer has no choices (request {RequestId})", provider.Name, Model.Id,
                reply.RequestId.Length > 0 ? reply.RequestId : "-");
            throw new ProviderException($"{provider.Name}: the answer had no choices.")
            {
                Code = "incomplete",
                ChargedUsage = usage.Prompt > 0 || usage.Output > 0 ? usage : null,
                RequestId = reply.RequestId,
            };
        }

        var text = JsonRead.Text(choice["message"]?["content"]);
        if (text.Length == 0 && JsonRead.Text(choice["message"]?["refusal"]).Length > 0)
        {
            throw new ProviderException($"{provider.Name}: the model refused.") { Code = "refusal", ChargedUsage = usage, RequestId = reply.RequestId };
        }

        return finish == "length"
            ? throw new ProviderException($"{provider.Name}: the answer was cut off.")
            {
                Code = "truncated",
                ChargedUsage = usage,
                RequestId = reply.RequestId,
                PartialText = text,
            }
            : new ChatResponse(text, usage, reported, finish, reply.RequestId);
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

    private Task<ProviderReply> PostAsync(JsonObject body, CancellationToken cancellationToken)
    {
        var json = body.ToJsonString();
        return http.SendAsync(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "chat/completions"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            provider.Authorize(message, apiKey);
            return message;
        }, provider.Name, apiKey, cancellationToken);
    }
}
