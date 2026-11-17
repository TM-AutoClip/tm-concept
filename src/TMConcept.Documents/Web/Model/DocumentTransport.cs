using System.Net;
using System.Net.Sockets;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Web;

/// <summary>How web pages and articles are fetched (tests give a fake handler; nothing real is fetched in tests).</summary>
public sealed record DocumentTransport(Func<HttpMessageHandler> CreateHandler)
{
    /// <summary>The longest one fetch may take, the body included (<see cref="DocumentLimits.WebSecondsMax"/>; tests use a short one).</summary>
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromSeconds(DocumentLimits.WebSecondsMax);

    /// <summary>
    /// The real network: a connection must be made in 15 s, and only to a public address (<see cref="NetworkAddresses"/>): the host's name is
    /// resolved here and the socket connects to the checked addresses only, so neither a redirect nor a name that resolves to a local address
    /// (DNS rebinding included) reaches this computer or the local network. Redirects are followed by the fetcher (each hop checked), and the
    /// connection is direct (no system proxy, which would hide the real address from the check).
    /// </summary>
    public static DocumentTransport Network { get; } = new(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = ConnectPublicAsync,
    });

    /// <summary>Connects to the public addresses of the request's host; any address of it that is not public refuses the connection.</summary>
    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(a => !NetworkAddresses.IsPublic(a)))
        {
            throw new DocumentRejectedException("private", "The address is on this computer or the local network.", host);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
