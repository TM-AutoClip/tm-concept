namespace TMConcept.Core.Tools;

/// <summary>Install progress: share 0–1 and a short text.</summary>
public sealed record ToolProgress(double Share, string Text);
