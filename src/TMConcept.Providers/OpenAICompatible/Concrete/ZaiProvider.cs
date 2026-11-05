using Microsoft.Extensions.Logging;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.OpenAICompatible;

/// <summary>Z.ai (GLM models, api.z.ai). Asks for json_object (json_schema support is not documented).</summary>
public sealed class ZaiProvider(ProviderHttp http, ILogger<ZaiProvider> logger) : OpenAICompatibleProviderBase(http, logger)
{
    /// <inheritdoc />
    public override string Code => "zai";

    /// <inheritdoc />
    public override string Name => "Z.ai";

    /// <inheritdoc />
    public override Uri Address { get; } = new("https://api.z.ai/api/paas/v4/");

    /// <inheritdoc />
    public override Uri KeyPage { get; } = new("https://z.ai/manage-apikey/apikey-list");

    /// <inheritdoc />
    public override bool JsonSchemaFormat => false;
}
