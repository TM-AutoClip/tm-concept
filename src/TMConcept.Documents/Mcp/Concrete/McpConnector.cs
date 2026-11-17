using Microsoft.Extensions.Logging;
using TMConcept.Core.Research;

namespace TMConcept.Documents.Mcp;

/// <summary>
/// Opens a session with an MCP server the user added (Settings → Research, advanced): nothing is started or called before the user
/// accepted the warning (<see cref="McpServerSettings.Confirmed"/>); a program is started without the app's environment
/// (<see cref="McpStdioChannel.Start"/>), an address is reached without redirects. The session is initialized before it is given back.
/// </summary>
public sealed class McpConnector(McpHttpTransport transport, ILoggerFactory loggers)
{
    /// <summary>A started, initialized session with <paramref name="server"/>; the caller disposes it. <see cref="McpException"/> with the reason.</summary>
    public async Task<McpClient> ConnectAsync(McpServerSettings server, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (!server.Confirmed)
        {
            throw new McpException("not_confirmed", "The warning about this MCP server has not been accepted.");
        }

        var logger = loggers.CreateLogger<McpClient>();
        IMcpChannel channel = server.Transport switch
        {
            McpTransportKind.Stdio => McpStdioChannel.Start(server.Command, server.Arguments, loggers.CreateLogger<McpStdioChannel>()),
            _ => Uri.TryCreate(server.Url.Trim(), UriKind.Absolute, out var address)
                ? new McpHttpChannel(address, transport, loggers.CreateLogger<McpHttpChannel>())
                : throw new McpException("http", "The MCP server's address is not a web address."),
        };
        var client = new McpClient(channel, logger);
        try
        {
            await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            // A session that did not start leaves no program running (the error itself goes up).
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
