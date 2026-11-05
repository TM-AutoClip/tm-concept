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
/// loose answer). No sampling parameters. <c>finish_reason: length</c> and a refusal are errors with the billed usage.
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
        var choice = node?["choices"]?[0];
        var text = JsonRead.Text(choice?["message"]?["content"]);
        if (text.Length == 0 && JsonRead.Text(choice?["message"]?["refusal"]).Length > 0)
        {
            throw new ProviderException($"{provider.Name}: the model refused.") { Code = "refusal", ChargedUsage = usage, RequestId = reply.RequestId };
        }

        var finish = JsonRead.Text(choice?["finish_reason"]);
        return finish == "length"
            ? throw new ProviderException($"{provider.Name}: the answer was cut off.") { Code = "truncated", ChargedUsage = usage, RequestId = reply.RequestId }
            : new ChatResponse(text, usage, reported, finish, reply.RequestId);
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
