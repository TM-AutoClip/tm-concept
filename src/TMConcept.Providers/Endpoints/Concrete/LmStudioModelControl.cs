using TMConcept.Core.Endpoints;

namespace TMConcept.Providers.Endpoints;

/// <summary>
/// Unloads a model from an LM Studio endpoint (every loaded instance through <c>/api/v1/models/unload</c>, then the model list until none is
/// left) when another local model needs the graphics card; any other server is left to the next control.
/// </summary>
public sealed class LmStudioModelControl(EndpointChatTransport transport) : IEndpointModelControl
{
    /// <inheritdoc />
    public Task<bool> UnloadAsync(LocalEndpoint endpoint, string modelId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.Kind != EndpointKind.LmStudio
            ? Task.FromResult(false)
            : transport.LmStudio.UnloadAsync(LocalServerAddress.Root(endpoint), endpoint.Token, modelId,
                endpoint.Name.Length > 0 ? endpoint.Name : LmStudioServer.DefaultName, cancellationToken);
    }
}
