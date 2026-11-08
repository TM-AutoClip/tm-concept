using Microsoft.Extensions.Logging;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.OpenAICompatible;

/// <summary>OpenAI (api.openai.com).</summary>
public sealed class OpenAIProvider(ProviderHttp http, ILogger<OpenAIProvider> logger) : OpenAICompatibleProviderBase(http, logger)
{
    /// <inheritdoc />
    public override string Code => "openai";

    /// <inheritdoc />
    public override string Name => "OpenAI";

    /// <inheritdoc />
    public override Uri Address { get; } = new("https://api.openai.com/v1/");

    /// <inheritdoc />
    public override Uri KeyPage { get; } = new("https://platform.openai.com/api-keys");

    /// <inheritdoc />
    public override string MaxTokensField => "max_completion_tokens";
}
