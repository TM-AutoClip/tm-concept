using TMConcept.Core.Engines;

namespace TMConcept.Core.Videos;

/// <summary>
/// A rendered silent video: the request it was rendered with (project folder, code, the input the engine got, output), whether the code was
/// repaired, and the video's own <paramref name="Input"/> with the real captions (the engine's input has none when subtitles are off, see
/// <see cref="Render.RenderInputBuilder.ForEngine"/>).
/// </summary>
public sealed record RenderOutcome(EngineRenderRequest Request, bool Repaired, Render.RenderInput Input)
{
    /// <summary>
    /// The engine's own code was rendered (<see cref="IVideoEngine.FallbackCode"/>): a local model's code failed the check after its repair.
    /// </summary>
    public bool AppCode { get; init; }
}
