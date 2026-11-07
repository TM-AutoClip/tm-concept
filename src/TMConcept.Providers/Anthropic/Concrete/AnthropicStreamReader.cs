using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TMConcept.Core.Chat;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Anthropic;

/// <summary>
/// Reads a Messages answer line by line while it streams (SSE), or a plain JSON answer (a body whose first line is not an event). Only
/// <c>text</c> blocks are joined (thinking blocks are skipped). Usage: <c>message_start.message.usage</c> (input, cache read, cache write)
/// and <c>message_delta.usage</c> (cumulative output). An unreadable event line is skipped and counted. <see cref="Answer"/> can be read
/// at any time: after a broken stream it holds what had arrived (the usage already billed).
/// </summary>
public sealed class AnthropicStreamReader
{
    private readonly StringBuilder _text = new();
    private readonly Dictionary<long, string> _blocks = [];
    private readonly StringBuilder _plain = new();
    private string _stop = string.Empty;
    private ChatUsage _usage = ChatUsage.Zero;
    private (string Type, string Raw)? _error;
    private int _skipped;
    private bool _events;
    private bool _plainBody;

    /// <summary>Adds one line of the body.</summary>
    public void Add(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (_plainBody)
        {
            _plain.Append(line).Append('\n');
            return;
        }

        var data = line.TrimEnd('\r');
        if (!_events && data.TrimStart().StartsWith('{'))
        {
            // Not an event stream: a plain JSON answer (an error body, or a provider that ignored "stream").
            _plainBody = true;
            _plain.Append(data).Append('\n');
            return;
        }

        if (data.StartsWith("event:", StringComparison.Ordinal))
        {
            _events = true;
            return;
        }

        if (!data.StartsWith("data:", StringComparison.Ordinal))
        {
            return;
        }

        _events = true;
        data = data[5..].Trim();
        JsonNode? e;
        try
        {
            e = data.Length == 0 ? null : JsonNode.Parse(data);
        }
        catch (JsonException)
        {
            // Counted: the client writes a warning (a lost text event would cut the answer).
            _skipped++;
            return;
        }

        switch (e?["type"]?.ToString())
        {
            case "message_start":
                _usage = Usage(e["message"]?["usage"], _usage);
                break;
            case "content_block_start":
                var type = e["content_block"]?["type"]?.ToString() ?? string.Empty;
                _blocks[JsonRead.Long(e["index"])] = type;
                if (type == "text")
                {
                    _text.Append(e["content_block"]?["text"]?.ToString());
                }

                break;
            case "content_block_delta":
                if (e["delta"]?["type"]?.ToString() == "text_delta" && _blocks.GetValueOrDefault(JsonRead.Long(e["index"])) == "text")
                {
                    _text.Append(e["delta"]?["text"]?.ToString());
                }

                break;
            case "message_delta":
                _stop = e["delta"]?["stop_reason"]?.ToString() ?? _stop;
                _usage = Usage(e["usage"], _usage);
                break;
            case "error":
                _error = (e["error"]?["type"]?.ToString() ?? "error", data);
                break;
        }
    }

    /// <summary>What has been read so far.</summary>
    public AnthropicAnswer Answer()
    {
        if (!_plainBody)
        {
            return new AnthropicAnswer(_text.ToString(), _stop, _usage, _error, _events, _skipped);
        }

        var body = _plain.ToString();
        var node = JsonNode.Parse(body);
        if (node?["type"]?.ToString() == "error")
        {
            return new AnthropicAnswer(string.Empty, string.Empty, ChatUsage.Zero, (node["error"]?["type"]?.ToString() ?? "error", body.Trim()), false);
        }

        var plain = new StringBuilder();
        foreach (var block in node?["content"]?.AsArray() ?? [])
        {
            if (block?["type"]?.ToString() == "text")
            {
                plain.Append(block["text"]?.ToString());
            }
        }

        return new AnthropicAnswer(plain.ToString(), node?["stop_reason"]?.ToString() ?? string.Empty, Usage(node?["usage"], null), null, false);
    }

    private static ChatUsage Usage(JsonNode? node, ChatUsage? previous)
    {
        var before = previous ?? ChatUsage.Zero;
        if (node is null)
        {
            return before;
        }

        return new ChatUsage(
            Math.Max(before.Input, JsonRead.Long(node["input_tokens"])),
            Math.Max(before.CacheRead, JsonRead.Long(node["cache_read_input_tokens"])),
            Math.Max(before.CacheWrite, JsonRead.Long(node["cache_creation_input_tokens"])),
            Math.Max(before.Output, JsonRead.Long(node["output_tokens"])));
    }
}
