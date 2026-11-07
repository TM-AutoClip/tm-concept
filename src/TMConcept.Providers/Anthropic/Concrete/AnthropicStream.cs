namespace TMConcept.Providers.Anthropic;

/// <summary>Reads a whole streamed (SSE) or plain JSON Messages answer at once (<see cref="AnthropicStreamReader"/> line by line).</summary>
public static class AnthropicStream
{
    /// <summary>Parses.</summary>
    public static AnthropicAnswer Parse(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var reader = new AnthropicStreamReader();
        foreach (var line in body.Split('\n'))
        {
            reader.Add(line);
        }

        return reader.Answer();
    }
}
