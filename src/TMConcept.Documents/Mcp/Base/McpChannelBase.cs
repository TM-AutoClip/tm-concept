using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Research;

namespace TMConcept.Documents.Mcp;

/// <summary>
/// The common part of an MCP channel: one message at a time (<see cref="SendAsync"/> waits for the one before), a message longer than
/// <see cref="ResearchLimits.McpMessageBytesMax"/> is refused, and a message the server sends while the app waits for its reply is sorted:
/// the reply to the app's id is returned, a request of the server ("ping") gets its answer (any other request "method not found": the app
/// offers the server nothing), a notification is skipped.
/// </summary>
public abstract class McpChannelBase(ILogger logger) : IMcpChannel
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The channel's log.</summary>
    protected ILogger Logger { get; } = logger;

    /// <summary>The protocol version agreed on (empty before).</summary>
    protected string ProtocolVersion { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task<string?> SendAsync(string message, long? id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExchangeAsync(message, id, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Negotiated(string protocolVersion) => ProtocolVersion = protocolVersion ?? string.Empty;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeCoreAsync().ConfigureAwait(false);
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>What a message from the server is for the app waiting for the reply <paramref name="id"/>.</summary>
    protected static (bool IsReply, string? Answer) Inspect(string json, long id)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new McpException("protocol", "The MCP server sent something that is not JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, null);
            }

            var hasId = root.TryGetProperty("id", out var idElement);
            if (hasId && !root.TryGetProperty("method", out _))
            {
                return (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out var number) && number == id, null);
            }

            if (hasId && root.TryGetProperty("method", out var method))
            {
                return (false, Answer(idElement, method.ValueKind == JsonValueKind.String && method.GetString() == "ping"));
            }

            return (false, null);
        }
    }

    /// <summary>Sends and, with an id, reads until the reply (the gate is held).</summary>
    protected abstract Task<string?> ExchangeAsync(string message, long? id, CancellationToken cancellationToken);

    /// <summary>Lets the channel's resources go.</summary>
    protected abstract ValueTask DisposeCoreAsync();

    /// <summary>A size error.</summary>
    protected static McpException TooLong() => new("size", $"An MCP message was longer than {ResearchLimits.McpMessageBytesMax >> 20} MB.");

    private static string Answer(JsonElement id, bool ping)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            if (ping)
            {
                writer.WriteStartObject("result");
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteStartObject("error");
                writer.WriteNumber("code", -32601);
                writer.WriteString("message", "Method not found");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
