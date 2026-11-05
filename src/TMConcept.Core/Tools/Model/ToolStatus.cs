namespace TMConcept.Core.Tools;

/// <summary>Whether a tool is ready and where.</summary>
public sealed record ToolStatus(ToolKind Tool, bool Ready, string Version, string ExecutablePath, string Source);
