namespace TMConcept.Core.Endpoints;

/// <summary>Asks a local server for its models (<c>GET {base}/models</c>, a short wait): the connection test of an endpoint.</summary>
public interface IEndpointProbe
{
    /// <summary>The server's models; a failure is a result with its code, never an exception (except cancellation).</summary>
    Task<EndpointListing> ListModelsAsync(string baseUrl, string token, CancellationToken cancellationToken);
}
