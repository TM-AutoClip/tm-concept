using Microsoft.Extensions.Logging;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.OpenAICompatible;

/// <summary>DeepSeek (api.deepseek.com). Asks for json_object.</summary>
public sealed class DeepSeekProvider(ProviderHttp http, ILogger<DeepSeekProvider> logger) : OpenAICompatibleProviderBase(http, logger)
{
    /// <inheritdoc />
    public override string Code => "deepseek";

    /// <inheritdoc />
    public override string Name => "DeepSeek";

    /// <inheritdoc />
    public override Uri Address { get; } = new("https://api.deepseek.com/");

    /// <inheritdoc />
    public override Uri KeyPage { get; } = new("https://platform.deepseek.com/api_keys");

    /// <inheritdoc />
    public override bool JsonSchemaFormat => false;
}
