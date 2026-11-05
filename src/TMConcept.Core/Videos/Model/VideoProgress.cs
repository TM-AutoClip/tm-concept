namespace TMConcept.Core.Videos;

/// <summary>Progress of one video: share 0–1 and the step code ("prepare", "speech", "timing", "images", "code", "render", "mux", "save").</summary>
public sealed record VideoProgress(double Share, string Step, string Detail = "");
