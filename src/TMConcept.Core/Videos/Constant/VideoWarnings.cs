namespace TMConcept.Core.Videos;

/// <summary>
/// Warning codes kept on a video (comma separated). Only expected, chosen situations are warnings: a failed voice or failed images stop the
/// video (<see cref="VideoException"/> "speech" / "images").
/// </summary>
public static class VideoWarnings
{
    /// <summary>Narration is off: the video is silent (yellow).</summary>
    public const string SpeechOff = "speech_off";

    /// <summary>AI voice (Supertonic, online): say so when publishing.</summary>
    public const string AiVoice = "ai_voice";

    /// <summary>The dictionary voice (Kokoro) left out words it does not know (names, rare words); the subtitles still show them.</summary>
    public const string SpeechSkipped = "speech_skipped";

    /// <summary>The code needed its one repair.</summary>
    public const string Repaired = "repaired";

    /// <summary>
    /// The local code model's code could not be used (it failed the check, or its repair failed to render): the app's own simple animation was
    /// rendered.
    /// </summary>
    public const string AppCode = "app_code";

    /// <summary>
    /// The local model's video did not show its scenes (an empty frame, or not the scene image; <see cref="FrameCheck"/>): the app's own
    /// animation was rendered instead.
    /// </summary>
    public const string FrameCheck = "frame_check";

    /// <summary>
    /// The kept video has scenes that showed an empty frame or not their image (<see cref="FrameCheck"/>): fewer than most of an online model's
    /// scenes, or the app's own animation (yellow; watch it before publishing).
    /// </summary>
    public const string FrameWeak = "frame_weak";

    /// <summary>No frame of the video that is not black was found for the thumbnail: the video may be empty (yellow).</summary>
    public const string NoThumbnail = "no_thumbnail";

    /// <summary>The motion check (<see cref="MotionCheck"/>, 0.1.41) found a sudden cut inside a scene (yellow; frame strip in the version's checks folder).</summary>
    public const string MotionCut = "motion_cut";

    /// <summary>The motion check found a freeze where the plan expects motion (yellow).</summary>
    public const string MotionFreeze = "motion_freeze";

    /// <summary>The motion check found black frames inside the film (yellow).</summary>
    public const string MotionBlack = "motion_black";

    /// <summary>The motion check found a short white flash (yellow).</summary>
    public const string MotionFlash = "motion_flash";

    /// <summary>The motion check found a hard cut to black at the end, without a fade (yellow).</summary>
    public const string MotionEndBlack = "motion_end_black";

    /// <summary>The motion check found a planned fade that did not happen (yellow).</summary>
    public const string MotionFade = "motion_fade";

    /// <summary>Every code of the motion check (made again for every version, never carried over).</summary>
    public static readonly IReadOnlyList<string> Motion = [MotionCut, MotionFreeze, MotionBlack, MotionFlash, MotionEndBlack, MotionFade];

    /// <summary>
    /// The subtitles' words were heard (whisper.cpp, 0.1.48) and some sentence matched the narration text by less than
    /// <see cref="Subtitles.CaptionTiming.MatchThreshold"/>: listen to it before publishing (yellow; the list with times is the version's
    /// <see cref="VideoFolders.Checks"/>/<see cref="VideoFolders.SubtitleCheckFile"/>).
    /// </summary>
    public const string SubtitleListen = "subtitle_listen";

    /// <summary>whisper.cpp is installed but could not hear a part: those subtitles kept their estimated word times (yellow).</summary>
    public const string SubtitleEstimated = "subtitle_estimated";

    /// <summary>Every code of the subtitles' timing (made again for every version, never carried over).</summary>
    public static readonly IReadOnlyList<string> Subtitles = [SubtitleListen, SubtitleEstimated];

    /// <summary>
    /// The sample images were chosen but the profile had none: the video was made without images. Written until 0.1.28 (sample images were
    /// removed in 0.1.29); read, never written.
    /// </summary>
    public const string SamplesNone = "samples_none";
}
