namespace TMConcept.Core.Videos;

/// <summary>
/// Warning codes kept on a video (comma separated). Only expected, chosen situations are warnings: a failed voice or failed images stop the
/// video since 16 Nov 2026 (<see cref="VideoException"/> "speech" / "images"), so nothing writes <see cref="SpeechFailed"/> or
/// <see cref="ImagesFailed"/> any more. They stay as codes of videos made by 0.1.15 and earlier (the database is carried over, so those
/// records still hold them and the Produced page must still name them).
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

    /// <summary>The local code model's code failed the check after its repair: the app's own simple animation was rendered.</summary>
    public const string AppCode = "app_code";

    /// <summary>
    /// The sample images were chosen but the profile had none: the video was made without images. Written until 0.1.28 (sample images were
    /// removed in 0.1.29); read, never written.
    /// </summary>
    public const string SamplesNone = "samples_none";

    /// <summary>Written by 0.1.15 and earlier only: narration failed and the video was made silent (red). Read, never written.</summary>
    public const string SpeechFailed = "speech_failed";

    /// <summary>Written by 0.1.15 and earlier only: the images failed and the video was made without them. Read, never written.</summary>
    public const string ImagesFailed = "images_failed";
}
