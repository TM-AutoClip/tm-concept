using Microsoft.Extensions.Logging;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Fal;

/// <summary>
/// fal (image and speech models through its queue at queue.fal.run; the key goes in <c>Authorization: Key …</c>). fal has no free request
/// to test a key with, so a key is saved untested and checked at its first use.
/// </summary>
public sealed class FalProvider(ProviderHttp http, ILogger<FalProvider> logger) : ProviderBase(http, logger)
{
    /// <inheritdoc />
    public override string Code => "fal";

    /// <inheritdoc />
    public override string Name => "fal";

    /// <inheritdoc />
    public override Uri Address { get; } = new("https://queue.fal.run/");

    /// <inheritdoc />
    public override Uri KeyPage { get; } = new("https://fal.ai/dashboard/keys");

    /// <inheritdoc />
    public override bool CanTest => false;

    /// <inheritdoc />
    public override void Authorize(HttpRequestMessage request, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.TryAddWithoutValidation("Authorization", "Key " + apiKey);
    }
}
