namespace TMConcept.Documents.Mcp;

/// <summary>
/// An MCP server that could not be used, with a code the page turns into the user's language: <c>start</c> (the program could not be
/// started), <c>closed</c> (it ended; <see cref="Exception.Message"/> holds the end of what it wrote to its error output), <c>http</c> (the
/// address answered an error), <c>rpc</c> (the server answered a protocol error), <c>tool</c> (the tool reported an error), <c>size</c> (a
/// message was too long), <c>protocol</c> (an answer that is not MCP), <c>not_confirmed</c> (the user has not accepted the warning yet).
/// </summary>
public sealed class McpException : Exception
{
    /// <summary>Creates it.</summary>
    public McpException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    /// <summary>Creates it without a code.</summary>
    public McpException()
        : this("protocol", "The MCP server could not be used.")
    {
    }

    /// <summary>Creates it with a message.</summary>
    public McpException(string message)
        : this("protocol", message)
    {
    }

    /// <summary>Creates it with a message and the cause.</summary>
    public McpException(string message, Exception inner)
        : this("protocol", message, inner)
    {
    }

    /// <summary>Why (see the class summary).</summary>
    public string Code { get; }
}
