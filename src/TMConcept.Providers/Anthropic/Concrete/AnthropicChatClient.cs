using System.Text;
using System.Text.Json.Nodes;
using TMConcept.Core.Chat;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Anthropic;

/// <summary>
/// Claude through <c>POST v1/messages</c>, streamed (a long answer must not hit a timeout). The stable system text carries a cache marker
/// when asked; a schema goes in <c>output_config.format</c> and an effort level in <c>output_config.effort</c>. No sampling parameters and no
/// thinking settings are sent (the model's defaults apply). The stop reason is checked before the text: <c>refusal</c>, <c>max_tokens</c>,
/// a context overflow and a stream that never finished are errors (with the tokens already billed and, for a cut, the text that arrived).
/// </summary>
public sealed class AnthropicChatClient(AnthropicProvider provider, ProviderHttp http, CatalogModel model, string apiKey) : IChatClient
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
            content.Add(new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.MimeType, ["data"] = Convert.ToBase64String(image.Data) },
            });
        }

        content.Add(new JsonObject { ["type"] = "text", ["text"] = request.User });
        var body = new JsonObject
        {
            ["model"] = Model.Id,
            ["max_tokens"] = Math.Min(request.MaxOutputTokens, Model.MaxOutputTokens > 0 ? Model.MaxOutputTokens : request.MaxOutputTokens),
            ["system"] = new JsonArray { system },
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = content } },
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
        var json = Body(request).ToJsonString();
        var reply = await http.SendAsync(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "messages"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            provider.Authorize(message, apiKey);
            return message;
        }, provider.Name, apiKey, cancellationToken).ConfigureAwait(false);

        var answer = AnthropicStream.Parse(reply.Text);
        if (answer.Error is { } error)
        {
            throw http.Failure(provider.Name, StatusOf(error.Type), error.Raw, reply.RequestId, apiKey);
        }

        return answer.StopReason switch
        {
            "refusal" => throw Charged("refusal", answer, reply.RequestId),
            "max_tokens" or "model_context_window_exceeded" => throw Charged("truncated", answer, reply.RequestId),
            "" when answer.Streamed => throw Charged("incomplete", answer, reply.RequestId),
            _ => new ChatResponse(answer.Text, answer.Usage, null, answer.StopReason, reply.RequestId),
        };
    }

    private ProviderException Charged(string code, AnthropicAnswer answer, string requestId) =>
        new($"{provider.Name}: the answer was not complete ({code}).")
        {
            Code = code,
            ChargedUsage = answer.Usage,
            RequestId = requestId,
            PartialText = code == "refusal" ? string.Empty : answer.Text,
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
