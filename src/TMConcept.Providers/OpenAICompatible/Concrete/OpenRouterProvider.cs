using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.OpenAICompatible;

/// <summary>
/// OpenRouter (openrouter.ai): reports the cost of each call in the usage, which is used as the exact cost. No <c>cache_control</c> is sent
/// (OpenRouter caches Anthropic models only on that opt-in), so its catalog lists no cache prices for them and the estimate assumes no
/// cache reads.
/// </summary>
public sealed class OpenRouterProvider(ProviderHttp http, ILogger<OpenRouterProvider> logger) : OpenAICompatibleProviderBase(http, logger)
{
    /// <inheritdoc />
    public override string Code => "openrouter";

    /// <inheritdoc />
    public override string Name => "OpenRouter";

    /// <inheritdoc />
    public override Uri Address { get; } = new("https://openrouter.ai/api/v1/");

    /// <inheritdoc />
    public override Uri KeyPage { get; } = new("https://openrouter.ai/settings/keys");

    /// <inheritdoc />
    protected override string TestPath => "key";

    /// <inheritdoc />
    public override void Extend(JsonObject body)
    {
        ArgumentNullException.ThrowIfNull(body);
        body["usage"] = new JsonObject { ["include"] = true };
    }
}
