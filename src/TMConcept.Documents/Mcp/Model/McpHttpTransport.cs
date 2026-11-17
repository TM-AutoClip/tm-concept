using System.Net;

namespace TMConcept.Documents.Mcp;

/// <summary>
/// How an MCP server's HTTP address is reached (tests give a fake handler; nothing real is called in tests). The user chose the address, so
/// one on this computer is allowed (a server they run themselves); redirects are never followed.
/// </summary>
public sealed record McpHttpTransport(Func<HttpMessageHandler> CreateHandler)
{
    /// <summary>The real network: a connection in 15 s, no redirects, compressed answers read.</summary>
    public static McpHttpTransport Network { get; } = new(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        AutomaticDecompression = DecompressionMethods.All,
    });
}
