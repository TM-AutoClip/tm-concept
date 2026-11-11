namespace TMConcept.Core.Videos;

/// <summary>
/// The steps of a video run as <see cref="VideoProgress"/> names them, in the order they happen (a run does only some of them: its plan).
/// The screen shows one row per step of <see cref="All"/>; a step that is not in the run's plan is shown as skipped.
/// </summary>
public static class VideoSteps
{
    /// <summary>Scenes read from the concept, the work folder made (or the kept project copied for a new version).</summary>
    public const string Prepare = "prepare";

    /// <summary>Narration, scene by scene.</summary>
    public const string Speech = "speech";

    /// <summary>Scene images (their prompts first when a model makes them).</summary>
    public const string Images = "images";

    /// <summary>The scenes timed to their clips (on this computer).</summary>
    public const string Timing = "timing";

    /// <summary>The video code from the code model, its safety check and one repair.</summary>
    public const string Code = "code";

    /// <summary>The engine renders the silent video.</summary>
    public const string Render = "render";

    /// <summary>ffmpeg joins the video, the narration and the subtitles.</summary>
    public const string Mux = "mux";

    /// <summary>The version folder, the thumbnail and the record.</summary>
    public const string Save = "save";

    /// <summary>Every step in the order of the work.</summary>
    public static readonly IReadOnlyList<string> All = [Prepare, Speech, Images, Timing, Code, Render, Mux, Save];
}
