namespace TMConcept.Core.Speech;

/// <summary>
/// How a delivery marker (0.1.35, <see cref="Markers.MarkerCatalog"/>: excited, calm, sad, serious, curious, whispering, a laugh, a sigh)
/// reaches each voice. Gemini: a mood is the turn's style text, a sound an inline angle tag; ElevenLabs v3/v4: an audio tag in square
/// brackets before the words. Every other voice reads the words as they are (the marker never reaches its text).
/// </summary>
public static class SpeechDeliveries
{
    /// <summary>Excited, energetic.</summary>
    public const string Excited = "excited";

    /// <summary>Calm, soothing.</summary>
    public const string Calm = "calm";

    /// <summary>Sad.</summary>
    public const string Sad = "sad";

    /// <summary>Serious, grave.</summary>
    public const string Serious = "serious";

    /// <summary>Curious.</summary>
    public const string Curious = "curious";

    /// <summary>Whispering.</summary>
    public const string Whisper = "whisper";

    /// <summary>A laugh before the words.</summary>
    public const string Laugh = "laugh";

    /// <summary>A sigh before the words.</summary>
    public const string Sigh = "sigh";

    /// <summary>Every delivery, in the order of the drop-down.</summary>
    public static readonly IReadOnlyList<string> All = [Excited, Calm, Sad, Serious, Curious, Whisper, Laugh, Sigh];

    /// <summary>Whether a delivery is a sound before the words (a laugh, a sigh) rather than a mood of them.</summary>
    public static bool IsSound(string? delivery) => delivery is Laugh or Sigh;

    /// <summary>Gemini's style text and inline tag of a delivery (empty for none or an unknown one).</summary>
    public static (string Style, string Tag) Gemini(string? delivery) => delivery switch
    {
        Excited => ("excited and energetic", string.Empty),
        Calm => ("calm and soothing", string.Empty),
        Sad => ("sad and quiet", string.Empty),
        Serious => ("serious and grave", string.Empty),
        Curious => ("curious and intrigued", string.Empty),
        Whisper => (string.Empty, "<whispers>"),
        Laugh => (string.Empty, "<laugh>"),
        Sigh => (string.Empty, "<sigh>"),
        _ => (string.Empty, string.Empty),
    };

    /// <summary>ElevenLabs' audio tag of a delivery (v3 and v4 models only; empty for the others, none or an unknown one).</summary>
    public static string ElevenLabs(string? delivery, string model)
    {
        if (model is null || !(model.StartsWith("eleven_v3", StringComparison.Ordinal) || model.StartsWith("eleven_v4", StringComparison.Ordinal)))
        {
            return string.Empty;
        }

        return delivery switch
        {
            Excited => "[excited]",
            Calm => "[calm]",
            Sad => "[sad]",
            Serious => "[serious]",
            Curious => "[curious]",
            Whisper => "[whispers]",
            Laugh => "[laughs]",
            Sigh => "[sighs]",
            _ => string.Empty,
        };
    }
}
