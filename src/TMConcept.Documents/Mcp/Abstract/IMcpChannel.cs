namespace TMConcept.Documents.Mcp;

/// <summary>
/// The way JSON-RPC messages reach an MCP server and its replies come back: a program's standard input and output (stdio) or Streamable
/// HTTP. A channel carries messages only; <see cref="McpClient"/> writes and reads them.
/// </summary>
public interface IMcpChannel : IAsyncDisposable
{
    /// <summary>
    /// Sends <paramref name="message"/>; with an <paramref name="id"/> waits for the reply with that id and returns it (requests and
    /// notifications of the server meanwhile are answered or skipped), without one returns null at once (a notification).
    /// <see cref="McpException"/> when the server cannot be reached or ended.
    /// </summary>
    Task<string?> SendAsync(string message, long? id, CancellationToken cancellationToken);

    /// <summary>The protocol version the server agreed to (sent with every later HTTP request).</summary>
    void Negotiated(string protocolVersion);
}
