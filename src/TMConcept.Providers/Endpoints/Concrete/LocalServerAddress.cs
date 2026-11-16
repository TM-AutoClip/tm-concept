using TMConcept.Core.Endpoints;

namespace TMConcept.Providers.Endpoints;

/// <summary>
/// Addresses of a local server: its root (the typed address without <c>/v1</c>, <c>/api/v1</c>, <c>/api/v0</c> or <c>/api</c>; Ollama's and
/// LM Studio's own API live there) and the base of its OpenAI-compatible API (<c>{root}/v1/</c> for LM Studio, the saved address otherwise).
/// Every result ends with a slash, so <c>new Uri(root, "api/tags")</c> keeps a path before it.
/// </summary>
public static class LocalServerAddress
{
    private static readonly string[] ApiSuffixes = ["/api/v1", "/api/v0", "/v1", "/api"];

    /// <summary>The server's root for a typed or saved address.</summary>
    public static Uri Root(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var path = address.AbsolutePath.TrimEnd('/');
        foreach (var suffix in ApiSuffixes)
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length];
                break;
            }
        }

        return new Uri(address.GetLeftPart(UriPartial.Authority) + path + "/");
    }

    /// <summary>The root of a saved endpoint (its address is checked when it is saved).</summary>
    public static Uri Root(LocalEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return EndpointAddress.TryParse(endpoint.BaseUrl, out var address)
            ? Root(address)
            : throw new InvalidOperationException("The local server's address is not a valid http or https address.");
    }

    /// <summary>The base of the OpenAI-compatible API of a saved endpoint (<c>…/v1/</c>).</summary>
    public static Uri ChatBase(LocalEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!EndpointAddress.TryParse(endpoint.BaseUrl, out var address))
        {
            throw new InvalidOperationException("The local server's address is not a valid http or https address.");
        }

        return endpoint.Kind is EndpointKind.Ollama or EndpointKind.LmStudio ? new Uri(Root(address), "v1/") : address;
    }

    /// <summary>The address saved for a server found at <paramref name="root"/>: without the trailing slash.</summary>
    public static string Saved(Uri root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.AbsoluteUri.TrimEnd('/');
    }
}
