namespace TMConcept.Core.Render;

/// <summary>
/// What a scene of the render input is built from: visual, narration, image, the narration length in milliseconds (pauses included) and how
/// the scene begins (<see cref="Scenes.SceneTransitions"/>).
/// </summary>
public sealed record RenderSceneSource(string Visual, string Caption, string Image, long AudioMs, string Transition = Scenes.SceneTransitions.None);
