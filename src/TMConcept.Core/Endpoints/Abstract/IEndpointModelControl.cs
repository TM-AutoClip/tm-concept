namespace TMConcept.Core.Endpoints;

/// <summary>
/// Unloads a model from a user-added server on this computer, so the graphics card is free for the next local model
/// (<see cref="Models.IGpuCoordinator"/>). A plain OpenAI-compatible server has no such call: register one control per server kind that has
/// one (Ollama: a request with <c>keep_alive: 0</c>; LM Studio: its unload call). The first control that handles the server wins.
/// </summary>
public interface IEndpointModelControl
{
    /// <summary>
    /// Asks <paramref name="endpoint"/> to unload <paramref name="modelId"/> and waits until it is gone; false when this control does not know
    /// the server (the next control is asked). Throws only for cancellation; a failed call is false with a log line.
    /// </summary>
    Task<bool> UnloadAsync(LocalEndpoint endpoint, string modelId, CancellationToken cancellationToken);
}