using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Models;
using TMConcept.Providers.Chat;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Anthropic;

/// <summary>Anthropic (Claude) through its own Messages API at api.anthropic.com; the key goes in the <c>x-api-key</c> header.</summary>
public sealed class AnthropicProvider(ProviderHttp http, ILogger<AnthropicProvider> logger) : ProviderBase(http, logger), IChatTransport
{
    /// <summary>API version header (the current version; it no longer accepts sampling parameters, none are sent).</summary>
    public const string ApiVersion = "2026-10-21";

    /// <inheritdoc />
    public override string Code => "anthropic";

    /// <inheritdoc />
    public override string Name => "Anthropic";

    /// <inheritdoc />
    public override Uri Address { get; } = new("https://api.anthropic.com/v1/");

    /// <inheritdoc />
    public override Uri KeyPage { get; } = new("https://platform.claude.com/settings/keys");

    /// <inheritdoc />
    public override void Authorize(HttpRequestMessage request, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
    }

    /// <inheritdoc />
    public IChatClient CreateChat(CatalogModel model, string apiKey) => new AnthropicChatClient(this, Http, model, apiKey, Logger);
}
