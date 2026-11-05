using Microsoft.Extensions.Logging;
using TMConcept.Core.Providers;

namespace TMConcept.Providers.Common;

/// <summary>
/// Shared body of a provider: the fixed address, the key test (a free GET with the key in the provider's own header) and the HTTP layer.
/// A subclass gives its address, test path and how the key is attached.
/// </summary>
public abstract class ProviderBase(ProviderHttp http, ILogger logger) : IProvider
{
    /// <inheritdoc />
    public abstract string Code { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract Uri Address { get; }

    /// <inheritdoc />
    public abstract Uri KeyPage { get; }

    /// <inheritdoc />
    public virtual bool CanTest => true;

    /// <summary>The HTTP layer.</summary>
    protected ProviderHttp Http { get; } = http;

    /// <summary>Logger of the provider.</summary>
    protected ILogger Logger { get; } = logger;

    /// <summary>Relative path of the free test request.</summary>
    protected virtual string TestPath => "models";

    /// <summary>Attaches the key to a request (each provider has its own header).</summary>
    public abstract void Authorize(HttpRequestMessage request, string apiKey);

    /// <inheritdoc />
    public async Task<ConnectionTest> TestAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (!CanTest)
        {
            return new ConnectionTest(true, "untested", string.Empty);
        }

        try
        {
            await Http.SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Address, TestPath));
                Authorize(request, apiKey);
                return request;
            }, Name, apiKey, cancellationToken).ConfigureAwait(false);
            Logger.LogInformation("{Provider}: key test succeeded", Name);
            return new ConnectionTest(true, "ok", string.Empty);
        }
        catch (ProviderException ex)
        {
            Logger.LogWarning("{Provider}: key test failed ({Code})", Name, ex.Code);
            return new ConnectionTest(false, ex.Code, ex.Message);
        }
    }
}
