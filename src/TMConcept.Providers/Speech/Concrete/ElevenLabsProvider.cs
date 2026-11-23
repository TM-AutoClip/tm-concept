using Microsoft.Extensions.Logging;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Speech;

/// <summary>ElevenLabs (0.1.35, speech): api.elevenlabs.io, the key in <c>xi-api-key</c>; the free key test lists the voices (<c>GET v2/voices</c>).</summary>
public sealed class ElevenLabsProvider(ProviderHttp http, ILogger<ElevenLabsProvider> logger) : ProviderBase(http, logger)
{
    /// <inheritdoc />
    public override string Code => "elevenlabs";

    /// <inheritdoc />
    public override string Name => "ElevenLabs";

    /// <inheritdoc />
    public override Uri Address { get; } = new("https://api.elevenlabs.io/v1/");

    /// <inheritdoc />
    public override Uri KeyPage { get; } = new("https://elevenlabs.io/app/settings/api-keys");

    /// <inheritdoc />
    protected override string TestPath => "../v2/voices?page_size=1";

    /// <inheritdoc />
    public override void Authorize(HttpRequestMessage request, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.TryAddWithoutValidation("xi-api-key", apiKey);
    }
}
