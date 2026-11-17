namespace TMConcept.Documents.Mcp;

/// <summary>A tool an MCP server lists: its name, description and the names of its arguments (the required ones too).</summary>
public sealed record McpTool(string Name, string Description, IReadOnlyList<string> Arguments, IReadOnlyList<string> Required);
