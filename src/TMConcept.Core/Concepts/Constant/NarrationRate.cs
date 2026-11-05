namespace TMConcept.Core.Concepts;

/// <summary>Estimated speaking rate (words per second) used for the narration length of a concept; corrected later by the measured voice.</summary>
public static class NarrationRate
{
    /// <summary>Turkish.</summary>
    public const double TurkishWordsPerSecond = 2.3;

    /// <summary>English.</summary>
    public const double EnglishWordsPerSecond = 2.6;

    /// <summary>Expected first-guess output tokens per concept (corrected by the profile's measured average).</summary>
    public const double FirstGuessOutputTokens = 700;
}
