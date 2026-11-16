using TMConcept.Core.Endpoints;

namespace TMConcept.Providers.Endpoints;

/// <summary>
/// Unloads a model from an Ollama endpoint (<c>keep_alive: 0</c>, then <c>/api/ps</c> until it is gone) when another local model needs the
/// graphics card; any other server is left to the next control.
/// </summary>
public sealed class OllamaModelControl(EndpointChatTransport transport) : IEndpointModelControl
{
    /// <inheritdoc />
    public async Task<EndpointUnload> UnloadAsync(LocalEndpoint endpoint, string modelId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Kind != EndpointKind.Ollama)
        {
            return EndpointUnload.NotMine;
        }

        return await transport.Ollama.UnloadAsync(LocalServerAddress.Root(endpoint), endpoint.Token, modelId,
            endpoint.Name.Length > 0 ? endpoint.Name : OllamaServer.DefaultName, cancellationToken).ConfigureAwait(false)
            ? EndpointUnload.Unloaded
            : EndpointUnload.Failed;
    }
}
