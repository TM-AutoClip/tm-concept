using Microsoft.Extensions.Logging;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.OpenAICompatible;

/// <summary>Mistral (api.mistral.ai).</summary>
public sealed class MistralProvider(ProviderHttp http, ILogger<MistralProvider> logger) : OpenAICompatibleProviderBase(http, logger)
{
    /// <inheritdoc />
    public override string Code => "mistral";

    /// <inheritdoc />
    public override string Name => "Mistral";

    /// <inheritdoc />
    public override Uri Address { get; } = new("https://api.mistral.ai/v1/");

    /// <inheritdoc />
    public override Uri KeyPage { get; } = new("https://console.mistral.ai/api-keys");
}
