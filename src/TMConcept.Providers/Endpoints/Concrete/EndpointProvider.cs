using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Endpoints;
using TMConcept.Providers.Common;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Providers.Endpoints;

/// <summary>
/// One user-added local server as an OpenAI-compatible provider, made for a call (not registered: the address comes from the user, unlike
/// the fixed addresses of the online providers). <c>max_tokens</c> carries the output limit (llama.cpp and Ollama ignore
/// <c>max_completion_tokens</c>); <c>json_schema</c> only for a model the user marked, otherwise <c>json_object</c>, and the client sends the
/// call once more without either after a 400/422. The token goes in a bearer header only when one is set. A 503 (model loading) is retried,
/// <c>&lt;think&gt;</c> blocks are removed from the answer (and from the live progress), a stream without usage is no warning (a local call
/// is free), and the answer limit is fitted into the context the user confirmed for the model.
/// </summary>
public sealed class EndpointProvider(LocalEndpoint endpoint, EndpointModel? model, EndpointTransport transport, ProviderHttp http, ILogger logger)
    : OpenAICompatibleProviderBase(http, logger)
{
    /// <summary>Name used in logs when the user gave none.</summary>
    public const string DefaultName = "Local server";

    // LM Studio's endpoint keeps the server's own address; its OpenAI-compatible API is under /v1.
    private readonly Uri _address = LocalServerAddress.ChatBase(endpoint);

    /// <inheritdoc />
    public override string Code => EndpointCodes.For(endpoint.Id);

    /// <inheritdoc />
    public override string Name => endpoint.Name.Length > 0 ? endpoint.Name : DefaultName;

    /// <inheritdoc />
    public override Uri Address => _address;

    /// <inheritdoc />
    public override Uri KeyPage => _address;

    /// <inheritdoc />
    public override bool JsonSchemaFormat => model?.JsonSchema ?? false;

    /// <inheritdoc />
    public override bool ReportsUsage => false;

    /// <inheritdoc />
    public override bool StripsThinking => true;

    /// <inheritdoc />
    public override bool FitsContext => true;

    /// <inheritdoc />
    public override int LoadingRetries => transport.LoadingRetries;

    /// <inheritdoc />
    public override void Authorize(HttpRequestMessage request, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    /// <inheritdoc />
    public override Task WaitForLoadingAsync(int attempt, CancellationToken cancellationToken) =>
        transport.Http.Delay(transport.LoadingDelay * attempt, cancellationToken);
}
