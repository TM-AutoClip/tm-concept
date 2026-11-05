namespace TMConcept.Core.Scenes;

/// <summary>A scene of a concept: what is shown (the bracket line) and what is said (the narration until the next bracket line).</summary>
public sealed record Scene(int Number, string Visual, string Narration);
