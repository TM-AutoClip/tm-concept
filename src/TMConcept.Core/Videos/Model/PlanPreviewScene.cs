namespace TMConcept.Core.Videos;

/// <summary>One scene of a plan preview: its number, its narration, what it stands on (image, map, diagram, title) and when it starts (seconds).</summary>
public sealed record PlanPreviewScene(int Number, string Caption, string Base, double StartSeconds);
