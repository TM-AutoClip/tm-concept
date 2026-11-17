namespace TMConcept.Core.Research;

/// <summary>
/// An MCP server the user added (Settings → Research, advanced): a program on this computer (<see cref="Command"/> and
/// <see cref="Arguments"/>) or a Streamable HTTP address (<see cref="Url"/>). The app calls only the two tools the user chose: one that
/// searches (<see cref="SearchTool"/> with the query in <see cref="SearchArgument"/>) and, optionally, one that fetches an address
/// (<see cref="FetchTool"/> with the address in <see cref="FetchArgument"/>). Their answers are data like any web page. Nothing is started
/// before the user confirmed the warning that a third-party server runs with their rights (<see cref="Confirmed"/>).
/// </summary>
public sealed class McpServerSettings
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The name the user gave.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>How the app talks to it.</summary>
    public McpTransportKind Transport { get; set; }

    /// <summary>The program (stdio).</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Its arguments, as typed (stdio; double quotes keep spaces).</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>The address (HTTP).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Used by research runs.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The user accepted the warning (a third-party server runs with their rights).</summary>
    public bool Confirmed { get; set; }

    /// <summary>The search tool.</summary>
    public string SearchTool { get; set; } = string.Empty;

    /// <summary>The search tool's argument that takes the query.</summary>
    public string SearchArgument { get; set; } = "query";

    /// <summary>The fetch tool (empty = the app fetches the found addresses itself).</summary>
    public string FetchTool { get; set; } = string.Empty;

    /// <summary>The fetch tool's argument that takes the address.</summary>
    public string FetchArgument { get; set; } = "url";
}
