namespace TMConcept.Core.Videos;

/// <summary>A picture the reviewer looks at (F11 + F14, 0.1.50): its scene, its file and the prompt it was made from.</summary>
public sealed record QualityPicture(int Scene, string File, string Prompt);
