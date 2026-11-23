using Microsoft.Extensions.Logging;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Speech;

/// <summary>
/// Google Cloud Text-to-Speech (0.1.35, Chirp 3: HD voices): texttospeech.googleapis.com, a Google Cloud API key with the Text-to-Speech API
/// allowed, in <c>x-goog-api-key</c>; the free key test lists the voices (<c>GET v1/voices</c>).
/// </summary>
public sealed class GoogleCloudProvider(ProviderHttp http, ILogger<GoogleCloudProvider> logger) : ProviderBase(http, logger)
{
    /// <inheritdoc />
    public override string Code => "google-cloud";

    /// <inheritdoc />
    public override string Name => "Google Cloud (Text-to-Speech)";

    /// <inheritdoc />
    public override Uri Address { get; } = new("https://texttospeech.googleapis.com/v1/");

    /// <inheritdoc />
    public override Uri KeyPage { get; } = new("https://console.cloud.google.com/apis/credentials");

    /// <inheritdoc />
    protected override string TestPath => "voices?languageCode=tr-TR";

    /// <inheritdoc />
    public override void Authorize(HttpRequestMessage request, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
    }
}
