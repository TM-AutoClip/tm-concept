namespace TMConcept.Core.Tools;

/// <summary>A pinned download: exact version, official address, SHA-256 and where the program sits inside the archive.</summary>
public sealed record ToolArchive(ToolKind Tool, string Version, string Rid, Uri Url, string Sha256, string Root, string ExecutablePath, long ApproximateBytes);
