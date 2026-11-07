namespace TMConcept.Core.Videos;

/// <summary>Warning codes kept on a video (comma separated).</summary>
public static class VideoWarnings
{
    /// <summary>Narration is off: the video is silent (yellow).</summary>
    public const string SpeechOff = "speech_off";

    /// <summary>Narration was tried but failed: the video is silent (red).</summary>
    public const string SpeechFailed = "speech_failed";

    /// <summary>Images were asked for but could not be made.</summary>
    public const string ImagesFailed = "images_failed";

    /// <summary>AI voice (Supertonic, online): say so when publishing.</summary>
    public const string AiVoice = "ai_voice";

    /// <summary>The dictionary voice (Kokoro) left out words it does not know (names, rare words); the subtitles still show them.</summary>
    public const string SpeechSkipped = "speech_skipped";

    /// <summary>The code needed its one repair.</summary>
    public const string Repaired = "repaired";

    /// <summary>The sample images were chosen but the profile has none: the video was made without images.</summary>
    public const string SamplesNone = "samples_none";
}
