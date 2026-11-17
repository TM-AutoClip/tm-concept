namespace TMConcept.Core.Research;

/// <summary>How the app talks to an MCP server the user added (stored as its number).</summary>
public enum McpTransportKind
{
    /// <summary>Streamable HTTP: the server's address.</summary>
    Http = 0,

    /// <summary>A program the app starts on this computer (it runs with the user's rights).</summary>
    Stdio = 1,
}
