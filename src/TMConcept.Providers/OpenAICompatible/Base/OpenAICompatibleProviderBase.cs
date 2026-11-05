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

    /// <summary>Extra request fields of this provider (OpenRouter asks for the cost in the usage).</summary>
    public virtual void Extend(JsonObject body)
    {
    }

    /// <inheritdoc />
    public IChatClient CreateChat(CatalogModel model, string apiKey) => new OpenAICompatibleChatClient(this, Http, model, apiKey, Logger);
}
