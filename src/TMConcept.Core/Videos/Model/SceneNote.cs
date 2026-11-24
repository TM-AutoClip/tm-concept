namespace TMConcept.Core.Videos;

/// <summary>A note of the user for one scene (1-based number): the plan preview's notes (F10) and the notes on a made video (F16).</summary>
public sealed record SceneNote(int Scene, string Text);
