namespace TMConcept.Core.Render;

/// <summary>What a scene of the render input is built from: visual, narration, image and the narration length in milliseconds.</summary>
public sealed record RenderSceneSource(string Visual, string Caption, string Image, long AudioMs);
