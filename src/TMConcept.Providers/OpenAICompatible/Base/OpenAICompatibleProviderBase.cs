using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Models;
using TMConcept.Providers.Chat;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.OpenAICompatible;

/// <summary>
/// Providers that speak OpenAI's <c>chat/completions</c> (OpenAI, OpenRouter, Z.ai, Mistral, DeepSeek): the same client, only the fixed
/// address and the key page differ. The key goes in a bearer header to that address only.
/// </summary>
public abstract class OpenAICompatibleProviderBase(ProviderHttp http, ILogger logger) : ProviderBase(http, logger), IChatTransport
{
    /// <summary>Whether the provider accepts <c>response_format: json_schema</c> (otherwise <c>json_object</c> is asked).</summary>
    public virtual bool JsonSchemaFormat => true;

    /// <inheritdoc />
    public override void Authorize(HttpRequestMessage request, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>The field that carries the output limit (<c>max_tokens</c>; OpenAI: <c>max_completion_tokens</c>).</summary>
    public virtual string MaxTokensField => "max_tokens";

    /// <summary>Puts a chosen effort into the request (<c>reasoning_effort</c>; OpenRouter: <c>reasoning.effort</c>).</summary>
    public virtual void SetEffort(JsonObject body, string effort)
    {
        ArgumentNullException.ThrowIfNull(body);
        body["reasoning_effort"] = effort;
    }

    /// <summary>Extra request fields of this provider (OpenRouter asks for the cost in the usage).</summary>
    public virtual void Extend(JsonObject body)
    {
    }

    /// <summary>
    /// Whether a stream is expected to report its usage (a missing one is warned about: the cost would be counted as 0). A free local server
    /// often sends none, and its cost is 0 anyway.
    /// </summary>
    public virtual bool ReportsUsage => true;

    /// <summary>
    /// Whether the answer may carry the model's thinking as <c>&lt;think&gt;…&lt;/think&gt;</c> text (local servers with some reasoning
    /// formats): it is removed before the answer is read as JSON (<see cref="ThinkBlocks"/>).
    /// </summary>
    public virtual bool StripsThinking => false;

    /// <summary>
    /// Whether the model's context window (<see cref="Core.Models.CatalogModel.ContextTokens"/>, as the user confirmed it) bounds a call: the
    /// answer limit is lowered to what the context leaves next to the prompt, and a prompt that does not fit at all fails before it is sent
    /// (a local server rejects it with a 400 only after processing it, which takes minutes on a consumer card, and the retry without the
    /// optional fields processed it again). An online provider's catalog context is its own business: false.
    /// </summary>
    public virtual bool FitsContext => false;

    /// <summary>
    /// How many times a 503 is retried (a local server answers 503 while it loads the model, which can take minutes); 0 = none (an online
    /// provider's 503 is a failure for the caller).
    /// </summary>
    public virtual int LoadingRetries => 0;

    /// <summary>The wait before retry <paramref name="attempt"/> (1-based) after a 503.</summary>
    public virtual Task WaitForLoadingAsync(int attempt, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public IChatClient CreateChat(CatalogModel model, string apiKey) => new OpenAICompatibleChatClient(this, Http, model, apiKey, Logger);
}
