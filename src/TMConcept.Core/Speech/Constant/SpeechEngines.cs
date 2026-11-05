namespace TMConcept.Core.Speech;

/// <summary>Speech choices stored on a profile.</summary>
public static class SpeechEngines
{
    /// <summary>No narration: the video is silent (a yellow warning in the log).</summary>
    public const string Off = "";

    /// <summary>Local Supertonic 3 (default).</summary>
    public const string Supertonic = "supertonic";

    /// <summary>Local Piper Turkish voice (dfki; non-commercial only).</summary>
    public const string PiperTurkish = "piper-tr";

    /// <summary>The online model chosen for the speech step.</summary>
    public const string Online = "online";
}
