using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Research;

namespace TMConcept.Documents.Mcp;

/// <summary>
/// An MCP server at an HTTP address (Streamable HTTP): every message is a POST of JSON; the reply comes as JSON or as an event stream
/// (<c>text/event-stream</c>, the reply in a <c>data:</c> event). The session id the server gives at the start (<c>Mcp-Session-Id</c>) and
/// the agreed protocol version (<c>MCP-Protocol-Version</c>) go with every later request. Redirects are not followed, a message longer than
/// the limit is refused, and only the host and the status go into the log.
/// </summary>
public sealed class McpHttpChannel(Uri address, McpHttpTransport transport, ILogger logger) : McpChannelBase(logger)
{
    private readonly HttpClient _client = new(transport.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    private string _session = string.Empty;

    /// <summary>The server's address.</summary>
    public Uri Address { get; } = address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp
        ? address
        : throw new McpException("http", "An MCP server address must start with http:// or https://.");

    /// <inheritdoc />
    protected override async Task<string?> ExchangeAsync(string message, long? id, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Address) { Content = new StringContent(message, Encoding.UTF8, "application/json") };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.UserAgent.ParseAdd(Web.SafeHttp.UserAgent);
        if (_session.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _session);
        }

        if (ProtocolVersion.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        }

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "The MCP server at {Host} could not be reached", Address.Host);
            throw new McpException("http", ex.Message, ex);
        }

        using (response)
        {
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessions) && sessions.FirstOrDefault() is { Length: > 0 } session)
            {
                _session = session;
            }

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("The MCP server at {Host} answered {Status}", Address.Host, (int)response.StatusCode);
                throw new McpException("http", string.Create(CultureInfo.InvariantCulture, $"The MCP server answered {(int)response.StatusCode}."));
            }

            if (id is not { } wanted)
            {
                return null;
            }

            var stream = response.Content.Headers.ContentType?.MediaType?.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase) == true;
            await using var body = new McpLimitedStream(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            using var reader = new StreamReader(body, Encoding.UTF8);
            return stream ? await ReadEventsAsync(reader, wanted, cancellationToken).ConfigureAwait(false) : await ReadJsonAsync(reader, wanted, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override ValueTask DisposeCoreAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<string> ReadJsonAsync(StreamReader reader, long id, CancellationToken cancellationToken)
    {
        var buffer = new char[16_384];
        var text = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > ResearchLimits.McpMessageBytesMax)
            {
                throw TooLong();
            }
        }

        var json = text.ToString();
        return Inspect(json, id).IsReply ? json : throw new McpException("protocol", "The MCP server's answer was not the reply to the request.");
    }

    private static async Task<string> ReadEventsAsync(StreamReader reader, long id, CancellationToken cancellationToken)
    {
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length > ResearchLimits.McpMessageBytesMax || data.Length > ResearchLimits.McpMessageBytesMax)
            {
                throw TooLong();
            }

            if (line.Length == 0)
            {
                if (data.Length > 0 && Inspect(data.ToString(), id).IsReply)
                {
                    return data.ToString();
                }

                data.Clear();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Append(data.Length > 0 ? "\n" : string.Empty).Append(line.AsSpan(5).TrimStart(' '));
            }
        }

        if (data.Length > 0 && Inspect(data.ToString(), id).IsReply)
        {
            return data.ToString();
        }

        throw new McpException("closed", "The MCP server's event stream ended without the reply.");
    }
}
