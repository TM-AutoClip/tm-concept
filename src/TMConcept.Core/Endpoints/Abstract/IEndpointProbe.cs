namespace TMConcept.Core.Endpoints;

/// <summary>
/// Talks to a local server outside a chat: its models (the connection test of an endpoint), the servers found on this computer and the
/// models a server holds in memory now.
/// </summary>
public interface IEndpointProbe
{
    /// <summary>
    /// The server's models (a short wait). The server's kind is found first: Ollama (<c>/api/version</c>) and LM Studio
    /// (<c>/api/v1/models</c>) are listed through their own API, any other server through <c>GET {base}/models</c>. A failure is a result with
    /// its code, never an exception (except cancellation).
    /// </summary>
    Task<EndpointListing> ListModelsAsync(string baseUrl, string token, CancellationToken cancellationToken);

    /// <summary>Ollama and LM Studio running on this computer at their usual addresses (a very short wait each); empty when none answers.</summary>
    Task<IReadOnlyList<DetectedServer>> DetectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The models <paramref name="endpoint"/> holds in memory now (Ollama: with the share on the graphics card); empty for a plain server or
    /// when it does not answer.
    /// </summary>
    Task<IReadOnlyList<LoadedServerModel>> LoadedAsync(LocalEndpoint endpoint, CancellationToken cancellationToken);
}
