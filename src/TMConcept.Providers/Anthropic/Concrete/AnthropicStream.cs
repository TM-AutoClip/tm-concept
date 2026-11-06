using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TMConcept.Core.Chat;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Anthropic;

/// <summary>
/// Reads a streamed (SSE) or plain JSON Messages answer. Only <c>text</c> blocks are joined (thinking blocks are skipped). Usage:
/// <c>message_start.message.usage</c> (input, cache read, cache write) and <c>message_delta.usage</c> (cumulative output). An unreadable event
/// line is skipped.
/// </summary>
public static class AnthropicStream
{
    /// <summary>Parses.</summary>
    public static AnthropicAnswer Parse(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            var node = JsonNode.Parse(trimmed);
            if (node?["type"]?.ToString() == "error")
            {
                return new AnthropicAnswer(string.Empty, string.Empty, ChatUsage.Zero, (node["error"]?["type"]?.ToString() ?? "error", trimmed), false);
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

        var text = new StringBuilder();
        var blocks = new Dictionary<long, string>();
        var stop = string.Empty;
        var usage = ChatUsage.Zero;
        (string, string)? error = null;
        var skipped = 0;
        foreach (var line in body.Split('\n'))
        {
            var data = line.TrimEnd('\r');
            if (!data.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            data = data[5..].Trim();
            JsonNode? e;
            try
            {
                e = data.Length == 0 ? null : JsonNode.Parse(data);
            }
            catch (JsonException)
            {
                // Counted: the client writes a warning (a lost text event would cut the answer).
                skipped++;
                continue;
            }

            switch (e?["type"]?.ToString())
            {
                case "message_start":
                    usage = Usage(e["message"]?["usage"], usage);
                    break;
                case "content_block_start":
                    var type = e["content_block"]?["type"]?.ToString() ?? string.Empty;
                    blocks[JsonRead.Long(e["index"])] = type;
                    if (type == "text")
                    {
                        text.Append(e["content_block"]?["text"]?.ToString());
                    }

                    break;
                case "content_block_delta":
                    if (e["delta"]?["type"]?.ToString() == "text_delta" && blocks.GetValueOrDefault(JsonRead.Long(e["index"])) == "text")
                    {
                        text.Append(e["delta"]?["text"]?.ToString());
                    }

                    break;
                case "message_delta":
                    stop = e["delta"]?["stop_reason"]?.ToString() ?? stop;
                    usage = Usage(e["usage"], usage);
                    break;
                case "error":
                    error = (e["error"]?["type"]?.ToString() ?? "error", data);
                    break;
            }
        }

        return new AnthropicAnswer(text.ToString(), stop, usage, error, true, skipped);
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
