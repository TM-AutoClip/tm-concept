using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Research;

namespace TMConcept.Documents.Mcp;

/// <summary>
/// A minimal MCP client (JSON-RPC 2.0) over an <see cref="IMcpChannel"/>: <see cref="InitializeAsync"/> (the app offers the server no
/// capability: no sampling, no roots, no elicitation), <see cref="ListToolsAsync"/> and <see cref="CallToolAsync"/>. Messages are written and
/// read with the JSON reader and writer only (no reflection), so the trimmed single-file app keeps working. Every answer has a time limit
/// (<see cref="ResearchLimits.McpSecondsMax"/>); a protocol error or a tool's error is an <see cref="McpException"/> with its reason. A
/// tool's text is DATA: the caller treats it like a fetched web page.
/// </summary>
public sealed class McpClient(IMcpChannel channel, ILogger logger) : IAsyncDisposable
{
    /// <summary>The protocol version the app asks for.</summary>
    public const string ProtocolVersion = "2025-06-18";

    private long _id;

    /// <summary>The longest wait for one answer (tests use a short one).</summary>
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromSeconds(ResearchLimits.McpSecondsMax);

    /// <summary>The server's name, once initialized.</summary>
    public string ServerName { get; private set; } = string.Empty;

    /// <summary>Starts the session: the server's protocol version is agreed and it is told the client is ready.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var result = await RequestAsync("initialize", writer =>
        {
            writer.WriteString("protocolVersion", ProtocolVersion);
            writer.WriteStartObject("capabilities");
            writer.WriteEndObject();
            writer.WriteStartObject("clientInfo");
            writer.WriteString("name", "TM-Concept");
            writer.WriteString("version", typeof(McpClient).Assembly.GetName().Version?.ToString(3) ?? "0");
            writer.WriteEndObject();
        }, cancellationToken).ConfigureAwait(false);
        var root = result.RootElement.GetProperty("result");
        var version = root.TryGetProperty("protocolVersion", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
        if (version.Length == 0)
        {
            throw new McpException("protocol", "The MCP server did not say its protocol version.");
        }

        ServerName = root.TryGetProperty("serverInfo", out var info) && info.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty;
        channel.Negotiated(version);
        await channel.SendAsync(Message("notifications/initialized", null, null), null, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("MCP session started (protocol {Version})", version);
    }

    /// <summary>The server's tools (every page of the list, at most ten pages).</summary>
    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var tools = new List<McpTool>();
        var cursor = string.Empty;
        for (var page = 0; page < 10; page++)
        {
            var after = cursor;
            using var result = await RequestAsync("tools/list", writer =>
            {
                if (after.Length > 0)
                {
                    writer.WriteString("cursor", after);
                }
            }, cancellationToken).ConfigureAwait(false);
            var root = result.RootElement.GetProperty("result");
            if (root.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                tools.AddRange(list.EnumerateArray().Select(Tool).OfType<McpTool>());
            }

            cursor = root.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() ?? string.Empty : string.Empty;
            if (cursor.Length == 0)
            {
                break;
            }
        }

        return tools;
    }

    /// <summary>
    /// Calls the tool <paramref name="name"/> with string <paramref name="arguments"/> and returns its text (the text parts joined; the
    /// structured content when there is no text), at most <see cref="ResearchLimits.ToolTextMax"/> characters. <see cref="McpException"/>
    /// "tool" when the tool reports an error.
    /// </summary>
    public async Task<string> CallToolAsync(string name, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        using var result = await RequestAsync("tools/call", writer =>
        {
            writer.WriteString("name", name);
            writer.WriteStartObject("arguments");
            foreach (var (key, value) in arguments)
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
        }, cancellationToken).ConfigureAwait(false);
        var root = result.RootElement.GetProperty("result");
        var text = new StringBuilder();
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.GetString() == "text" && part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    text.Append(text.Length > 0 ? "\n\n" : string.Empty).Append(value.GetString());
                }
                else if (part.TryGetProperty("resource", out var resource) && resource.TryGetProperty("text", out var resourceText) && resourceText.ValueKind == JsonValueKind.String)
                {
                    text.Append(text.Length > 0 ? "\n\n" : string.Empty).Append(resourceText.GetString());
                }
            }
        }

        if (text.Length == 0 && root.TryGetProperty("structuredContent", out var structured))
        {
            text.Append(structured.GetRawText());
        }

        var answer = text.Length > ResearchLimits.ToolTextMax ? text.ToString(0, ResearchLimits.ToolTextMax) : text.ToString();
        if (root.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True)
        {
            logger.LogWarning("MCP tool {Tool} reported an error", name);
            throw new McpException("tool", answer.Length > 0 ? Shorten(answer) : "The tool reported an error.");
        }

        logger.LogInformation("MCP tool {Tool} answered {Characters} characters", name, answer.Length);
        return answer;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => channel.DisposeAsync();

    private static McpTool? Tool(JsonElement tool)
    {
        if (!tool.TryGetProperty("name", out var nameElement) || nameElement.GetString() is not { Length: > 0 } name)
        {
            return null;
        }

        var description = tool.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? string.Empty : string.Empty;
        var arguments = new List<string>();
        var required = new List<string>();
        if (tool.TryGetProperty("inputSchema", out var schema) && schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
            {
                arguments.AddRange(properties.EnumerateObject().Select(p => p.Name));
            }

            if (schema.TryGetProperty("required", out var needed) && needed.ValueKind == JsonValueKind.Array)
            {
                required.AddRange(needed.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!));
            }
        }

        return new McpTool(name, description, arguments, required);
    }

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";

    private static string Message(string method, long? id, Action<Utf8JsonWriter>? parameters)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            if (id is { } number)
            {
                writer.WriteNumber("id", number);
            }

            writer.WriteString("method", method);
            if (parameters is not null)
            {
                writer.WriteStartObject("params");
                parameters(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private async Task<JsonDocument> RequestAsync(string method, Action<Utf8JsonWriter> parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _id);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeLimit);
        string reply;
        try
        {
            reply = await channel.SendAsync(Message(method, id, parameters), id, limit.Token).ConfigureAwait(false)
                ?? throw new McpException("protocol", "The MCP server gave no reply.");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("MCP {Method} got no answer within {Seconds} s", method, TimeLimit.TotalSeconds);
            throw new McpException("closed", "The MCP server did not answer in time.", ex);
        }

        var document = JsonDocument.Parse(reply);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? string.Empty : string.Empty;
            document.Dispose();
            logger.LogWarning("MCP {Method} was answered with a protocol error", method);
            throw new McpException("rpc", message.Length > 0 ? Shorten(message) : "The MCP server answered an error.");
        }

        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new McpException("protocol", "The MCP server's reply has no result.");
        }

        return document;
    }
}
