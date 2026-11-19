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

    /// <summary>The local code model's code failed the check after its repair: the app's own simple animation was rendered.</summary>
    public const string AppCode = "app_code";

    /// <summary>The sample images were chosen but the profile has none: the video was made without images.</summary>
    public const string SamplesNone = "samples_none";
}
