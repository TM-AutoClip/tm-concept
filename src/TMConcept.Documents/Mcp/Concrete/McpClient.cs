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
/// (<see cref="ResearchLimits.McpSecondsMax"/>); a protocol error or a tool's error is an <see cref="McpException"/> with its reason. Every
/// field of an answer is read with its kind checked, and an answer of the wrong shape (a number where a name belongs, a reply that is not
/// JSON) is an <see cref="McpException"/> "protocol" too, so a third-party server can only fail its own backend. A tool's text is DATA: the
/// caller treats it like a fetched web page.
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
    public Task InitializeAsync(CancellationToken cancellationToken) => ReadingAsync("initialize", async () =>
    {
        await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });

    /// <summary>The server's tools (every page of the list, at most ten pages).</summary>
    public Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken cancellationToken) => ReadingAsync("tools/list", () => ListToolsCoreAsync(cancellationToken));

    /// <summary>
    /// Calls the tool <paramref name="name"/> with string <paramref name="arguments"/> and returns its text (the text parts joined; the
    /// structured content when there is no text), at most <see cref="ResearchLimits.ToolTextMax"/> characters. <see cref="McpException"/>
    /// "tool" when the tool reports an error.
    /// </summary>
    public Task<string> CallToolAsync(string name, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        return ReadingAsync("tools/call", () => CallToolCoreAsync(name, arguments, cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => channel.DisposeAsync();

    /// <summary>The string property <paramref name="name"/> of <paramref name="element"/>; empty when it is missing or not a string.</summary>
    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static McpTool? Tool(JsonElement tool)
    {
        if (Text(tool, "name") is not { Length: > 0 } name)
        {
            return null;
        }

        var description = Text(tool, "description");
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

    /// <summary>Runs one exchange; an answer the JSON reader cannot take (wrong shape, not JSON) is a protocol error of this server.</summary>
    private async Task<T> ReadingAsync<T>(string method, Func<Task<T>> exchange)
    {
        try
        {
            return await exchange().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or JsonException or FormatException)
        {
            logger.LogWarning(ex, "MCP {Method} got an answer of the wrong shape", method);
            throw new McpException("protocol", "The MCP server's answer could not be read.", ex);
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
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
        var version = Text(root, "protocolVersion");
        if (version.Length == 0)
        {
            throw new McpException("protocol", "The MCP server did not say its protocol version.");
        }

        ServerName = root.TryGetProperty("serverInfo", out var info) ? Text(info, "name") : string.Empty;
        channel.Negotiated(version);
        await channel.SendAsync(Message("notifications/initialized", null, null), null, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("MCP session started (protocol {Version})", version);
    }

    private async Task<IReadOnlyList<McpTool>> ListToolsCoreAsync(CancellationToken cancellationToken)
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

            cursor = Text(root, "nextCursor");
            if (cursor.Length == 0)
            {
                break;
            }
        }

        return tools;
    }

    private async Task<string> CallToolCoreAsync(string name, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken)
    {
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
                var piece = Text(part, "type") == "text" ? Text(part, "text")
                    : part.ValueKind == JsonValueKind.Object && part.TryGetProperty("resource", out var resource) ? Text(resource, "text") : string.Empty;
                if (piece.Length > 0)
                {
                    text.Append(text.Length > 0 ? "\n\n" : string.Empty).Append(piece);
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
        if (root.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new McpException("protocol", "The MCP server's reply is not a JSON object.");
        }

        if (root.TryGetProperty("error", out var error))
        {
            var message = Text(error, "message");
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
