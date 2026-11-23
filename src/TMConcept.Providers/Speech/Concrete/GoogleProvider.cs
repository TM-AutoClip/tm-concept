using Microsoft.Extensions.Logging;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Speech;

/// <summary>
/// Google's Gemini API (0.1.35, speech only): generativelanguage.googleapis.com, the key in <c>x-goog-api-key</c>; the free key test lists
/// the models (<c>GET v1beta/models</c>).
/// </summary>
public sealed class GoogleProvider(ProviderHttp http, ILogger<GoogleProvider> logger) : ProviderBase(http, logger)
{
    /// <inheritdoc />
    public override string Code => "google";

    /// <inheritdoc />
    public override string Name => "Google (Gemini API)";

    /// <inheritdoc />
    public override Uri Address { get; } = new("https://generativelanguage.googleapis.com/v1beta/");

    /// <inheritdoc />
    public override Uri KeyPage { get; } = new("https://aistudio.google.com/apikey");

    /// <inheritdoc />
    public override void Authorize(HttpRequestMessage request, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
    }
}
