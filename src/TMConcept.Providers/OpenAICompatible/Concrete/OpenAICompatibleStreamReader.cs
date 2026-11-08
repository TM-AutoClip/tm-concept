using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TMConcept.Providers.OpenAICompatible;

/// <summary>
/// Reads a streamed <c>chat/completions</c> answer (SSE chunks: <c>choices[0].delta.content</c>, the <c>finish_reason</c>, the usage of
/// the last chunk, an <c>error</c> object, <c>data: [DONE]</c>) line by line and gives it back in the shape of a plain answer
/// (<c>choices[0].message</c>, <c>finish_reason</c>, <c>usage</c>, <c>error</c>), so the client checks both the same way. A body that is not
/// an event stream (a provider that ignored <c>stream</c>) is kept whole and returned as it is. Comment lines (": OPENROUTER PROCESSING")
/// and unreadable chunks are skipped; the latter are counted.
/// </summary>
public sealed class OpenAICompatibleStreamReader
{
    private readonly StringBuilder _content = new();
    private readonly StringBuilder _refusal = new();
    private readonly StringBuilder _plain = new();
    private string _finish = string.Empty;
    private JsonNode? _usage;
    private JsonNode? _error;
    private JsonNode? _choiceError;
    private bool _choice;
    private bool _events;
    private bool _plainBody;

    /// <summary>Chunks that could not be read.</summary>
    public int Skipped { get; private set; }

    /// <summary>Whether the answer was an event stream.</summary>
    public bool Streamed => _events && !_plainBody;

    /// <summary>Characters of visible text read so far (0 while the model only reasons; the progress tells thinking from writing by it).</summary>
    public int TextLength => _content.Length;

    /// <summary>The visible text read so far.</summary>
    public string Text => _content.ToString();

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
            _plainBody = true;
            _plain.Append(data).Append('\n');
            return;
        }

        if (!data.StartsWith("data:", StringComparison.Ordinal))
        {
            return;
        }

        _events = true;
        data = data[5..].Trim();
        if (data.Length == 0 || data == "[DONE]")
        {
            return;
        }

        JsonNode? chunk;
        try
        {
            chunk = JsonNode.Parse(data);
        }
        catch (JsonException)
        {
            Skipped++;
            return;
        }

        if (chunk?["error"] is { } error)
        {
            _error = error.DeepClone();
        }

        if (chunk?["usage"] is JsonObject usage)
        {
            _usage = usage.DeepClone();
        }

        if ((chunk?["choices"] as JsonArray) is not { Count: > 0 } choices)
        {
            return;
        }

        _choice = true;
        var choice = choices[0];
        _content.Append(choice?["delta"]?["content"] is JsonValue c && c.TryGetValue<string>(out var text) ? text : string.Empty);
        _refusal.Append(choice?["delta"]?["refusal"] is JsonValue r && r.TryGetValue<string>(out var refusal) ? refusal : string.Empty);
        if (choice?["finish_reason"] is JsonValue f && f.TryGetValue<string>(out var finish) && finish.Length > 0)
        {
            _finish = finish;
        }

        if (choice?["error"] is { } choiceError)
        {
            _choiceError = choiceError.DeepClone();
        }
    }

    /// <summary>The answer read so far, shaped like a plain <c>chat/completions</c> answer.</summary>
    public JsonNode? Answer()
    {
        if (_plainBody)
        {
            return JsonNode.Parse(_plain.ToString());
        }

        var answer = new JsonObject();
        var choices = new JsonArray();
        if (_choice)
        {
            var message = new JsonObject { ["content"] = _content.ToString() };
            if (_refusal.Length > 0)
            {
                message["refusal"] = _refusal.ToString();
            }

            var choice = new JsonObject { ["message"] = message, ["finish_reason"] = _finish };
            if (_choiceError is not null)
            {
                choice["error"] = _choiceError.DeepClone();
            }

            choices.Add((JsonNode)choice);
        }

        answer["choices"] = choices;
        if (_usage is not null)
        {
            answer["usage"] = _usage.DeepClone();
        }

        if (_error is not null)
        {
            answer["error"] = _error.DeepClone();
        }

        return answer;
    }
}
