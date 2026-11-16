using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TMConcept.Core.Chat;

/// <summary>
/// Reads the JSON of a model's answer tolerantly (0.1.17: small local models wrap it in thinking, code fences and prose, or write it
/// slightly wrong). Steps: thinking blocks (<c>&lt;think&gt;…&lt;/think&gt;</c>, <c>&lt;thinking&gt;</c>) are removed; a code fence that holds
/// JSON is preferred; the first balanced <c>{…}</c> or <c>[…]</c> is taken (text before and after is ignored; strings and escapes are
/// respected); then it is parsed with trailing commas and comments allowed, and once more after <see cref="Repair"/> (raw line breaks and tabs
/// inside strings escaped, typographic quotes around keys and values made plain, trailing commas removed). The answer is untrusted data:
/// nothing here runs or follows it. Used by topics, concepts, the AI edit and the image prompts, together with <see cref="JsonAnswerRetry"/>.
/// </summary>
public static partial class ModelJson
{
    private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 };

    /// <summary>The answer without its thinking blocks; an unclosed block at the start (an answer cut while thinking) leaves nothing.</summary>
    public static string StripThinking(string? text)
    {
        var value = Think().Replace(text ?? string.Empty, string.Empty);
        var open = OpenThink().Match(value);
        return open.Success && value[..open.Index].Trim().Length == 0 ? string.Empty : value;
    }

    /// <summary>
    /// The JSON part of an answer: the first balanced object or array (inside the first code fence that holds one, when there is such a fence);
    /// when it never closes (a cut answer), everything from its start. Null when the answer holds no <c>{</c> or <c>[</c>.
    /// </summary>
    public static string? Extract(string? text)
    {
        var value = StripThinking(text).Replace("\0", string.Empty, StringComparison.Ordinal);
        foreach (Match fence in Fence().Matches(value))
        {
            var inner = fence.Groups["body"].Value;
            if (Start(inner) >= 0)
            {
                value = inner;
                break;
            }
        }

        var start = Start(value);
        if (start < 0)
        {
            return null;
        }

        var end = End(value, start);
        return end < 0 ? value[start..] : value[start..(end + 1)];
    }

    /// <summary>Whether the JSON of the answer closes (false: it was cut off, or there is none).</summary>
    public static bool IsComplete(string? text)
    {
        var json = Extract(text);
        return json is not null && End(json, 0) == json.Length - 1;
    }

    /// <summary>The answer's JSON as a node; null when it cannot be read even after <see cref="Repair"/>.</summary>
    public static JsonNode? TryParse(string? text)
    {
        if (Extract(text) is not { } json)
        {
            return null;
        }

        return Parse(json) ?? Parse(Repair(json));
    }

    /// <summary>The answer's JSON object; null when it is not readable or not an object.</summary>
    public static JsonObject? TryParseObject(string? text) => TryParse(text) as JsonObject;

    /// <summary>
    /// Repairs what small models often get wrong: raw line breaks and tabs inside strings (escaped), typographic double quotes used as JSON
    /// quotes outside strings (made plain), commas right before <c>}</c> or <c>]</c> (removed), control characters (removed).
    /// </summary>
    public static string Repair(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var output = new StringBuilder(json.Length + 16);
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                switch (c)
                {
                    case '\\' when i + 1 < json.Length:
                        output.Append(c).Append(json[++i]);
                        break;
                    case '"':
                        inString = false;
                        output.Append(c);
                        break;
                    case '\u201D' or '\u201C' when NextSignificant(json, i + 1) is ':' or ',' or '}' or ']' or '\0':
                        inString = false;
                        output.Append('"');
                        break;
                    case '\n':
                        output.Append("\\n");
                        break;
                    case '\r':
                        break;
                    case '\t':
                        output.Append("\\t");
                        break;
                    default:
                        if (!char.IsControl(c))
                        {
                            output.Append(c);
                        }

                        break;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                case '“':
                case '”':
                    inString = true;
                    output.Append('"');
                    break;
                case ',' when NextSignificant(json, i + 1) is '}' or ']':
                    break;
                default:
                    if (!char.IsControl(c) || char.IsWhiteSpace(c))
                    {
                        output.Append(c);
                    }

                    break;
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// The complete string items of the array under <paramref name="key"/> (<c>"prompts": ["a", "b", "c…</c>): every item whose closing quote
    /// arrived, in order, so a cut answer still gives what came before the cut; stops at the first item that is not a string.
    /// </summary>
    public static IReadOnlyList<string> StringItems(string? text, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var value = StripThinking(text);
        var at = value.IndexOf('"' + key + '"', StringComparison.Ordinal);
        var start = at < 0 ? -1 : value.IndexOf('[', at);
        var found = new List<string>();
        if (start < 0)
        {
            return found;
        }

        var position = start + 1;
        while (position < value.Length)
        {
            while (position < value.Length && (char.IsWhiteSpace(value[position]) || value[position] == ','))
            {
                position++;
            }

            if (position >= value.Length || value[position] != '"')
            {
                break;
            }

            var end = ClosingQuote(value, position + 1);
            if (end < 0 || Parse(Repair(value[position..(end + 1)])) is not JsonValue item || !item.TryGetValue<string>(out var itemText))
            {
                break;
            }

            found.Add(itemText);
            position = end + 1;
        }

        return found;
    }

    private static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: Lenient);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The first <c>{</c> or <c>[</c>; -1 when there is none.</summary>
    private static int Start(string text)
    {
        var brace = text.IndexOf('{', StringComparison.Ordinal);
        var bracket = text.IndexOf('[', StringComparison.Ordinal);
        return brace < 0 ? bracket : bracket < 0 ? brace : Math.Min(brace, bracket);
    }

    /// <summary>The index of the bracket that closes the one at <paramref name="start"/>; -1 when it never closes.</summary>
    private static int End(string text, int start)
    {
        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{' or '[':
                    depth++;
                    break;
                case '}' or ']':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    private static int ClosingQuote(string text, int from)
    {
        for (var i = from; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == '"')
            {
                return i;
            }
        }

        return -1;
    }

    private static char NextSignificant(string text, int from)
    {
        for (var i = from; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return text[i];
            }
        }

        return '\0';
    }

    [GeneratedRegex(@"<(think|thinking|reasoning)>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Think();

    [GeneratedRegex(@"<(think|thinking|reasoning)>", RegexOptions.IgnoreCase)]
    private static partial Regex OpenThink();

    [GeneratedRegex(@"```[a-zA-Z0-9_-]*[ \t]*\r?\n(?<body>.*?)```", RegexOptions.Singleline)]
    private static partial Regex Fence();
}
