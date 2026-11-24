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
    /// The engine's own code was rendered (<see cref="IVideoEngine.FallbackCode"/>): a local model's code failed the check, or its repaired code
    /// failed to render, or its video failed the frame check (<see cref="FrameFallback"/>).
    /// </summary>
    public bool AppCode { get; init; }

    /// <summary>The rendered video failed the frame check (<see cref="FrameCheck"/>) and the app's own animation was rendered instead (a local model).</summary>
    public bool FrameFallback { get; init; }

    /// <summary>
    /// The kept video has scenes that failed the frame check: an online model's video with fewer than <see cref="FrameCheckLimits.StopShare"/>
    /// of its scenes failing, or the app's own animation.
    /// </summary>
    public bool FrameWeak { get; init; }

    /// <summary>The music chosen and laid under the scenes during the plan (0.1.45, <see cref="PlanContext.Music"/>); null otherwise.</summary>
    public PreparedMusic? Music { get; init; }

    /// <summary>The warning codes a render gives (each video run sets them from here, and a new version drops the old ones first).</summary>
    public static IReadOnlyList<string> Codes { get; } = [VideoWarnings.Repaired, VideoWarnings.AppCode, VideoWarnings.FrameCheck, VideoWarnings.FrameWeak];

    /// <summary>
    /// The warnings of this render: repaired, the app's own animation (left out after a frame fallback, whose own warning says it), the frame
    /// fallback and the weak frames.
    /// </summary>
    public IEnumerable<string> Warnings()
    {
        if (Repaired)
        {
            yield return VideoWarnings.Repaired;
        }

        if (AppCode && !FrameFallback)
        {
            yield return VideoWarnings.AppCode;
        }

        if (FrameFallback)
        {
            yield return VideoWarnings.FrameCheck;
        }

        if (FrameWeak)
        {
            yield return VideoWarnings.FrameWeak;
        }
    }
}
