namespace TMConcept.Core.Endpoints;

/// <summary>
/// Unloads a model from a user-added server on this computer, so the graphics card is free for the next local model
/// (<see cref="Models.IGpuCoordinator"/>). A plain OpenAI-compatible server has no such call: register one control per server kind that has
/// one (Ollama: a request with <c>keep_alive: 0</c>; LM Studio: its unload call). The first control that handles the server wins.
/// </summary>
public interface IEndpointModelControl
{
    /// <summary>
    /// Asks <paramref name="endpoint"/> to unload <paramref name="modelId"/> and waits until it is gone: <see cref="EndpointUnload.NotMine"/>
    /// when this control does not know the server (the next control is asked), <see cref="EndpointUnload.Failed"/> (with a log line) when the
    /// server refused or still holds it. Throws only for cancellation.
    /// </summary>
    Task<EndpointUnload> UnloadAsync(LocalEndpoint endpoint, string modelId, CancellationToken cancellationToken);
}
