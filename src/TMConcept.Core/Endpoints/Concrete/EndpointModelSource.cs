using System.Net;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;

namespace TMConcept.Core.Endpoints;

/// <summary>
/// The local models of the user-added servers: a server whose address is this computer (localhost, 127.x, ::1) gives an
/// <see cref="EndpointLocalModel"/>; one on another computer is an online model for this app (it takes no card here).
/// </summary>
public sealed class EndpointModelSource(EndpointRegistry registry, IEnumerable<IEndpointModelControl> controls, ILogger<EndpointModelSource> logger) : ILocalModelSource
{
    private readonly IReadOnlyList<IEndpointModelControl> _controls = controls.ToList();

    /// <inheritdoc />
    public ILocalModel? Create(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!EndpointCodes.TryId(model.Provider, out var id) || registry.Find(id) is not { } endpoint || !OnThisComputer(endpoint.BaseUrl))
        {
            return null;
        }

        return new EndpointLocalModel(model, registry, _controls, logger);
    }

    /// <summary>Whether <paramref name="address"/> points at this computer (a loopback name or address).</summary>
    public static bool OnThisComputer(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.IdnHost.Trim('[', ']');
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));
    }
}