namespace TMConcept.Core.Costs;

/// <summary>Rules of thumb for estimates before a call (corrected by measured averages once there is history).</summary>
public static class TokenRules
{
    /// <summary>Characters per token in Turkish text (rough; newer tokenizers count more tokens).</summary>
    public const double TurkishCharactersPerToken = 2.6;

    /// <summary>Characters per token in English text.</summary>
    public const double EnglishCharactersPerToken = 3.6;

    /// <summary>First guess of output tokens per concept.</summary>
    public const int ConceptOutputTokens = 700;

    /// <summary>Lowest thinking surcharge on output.</summary>
    public const double ThinkingMin = 1.1;

    /// <summary>Highest thinking surcharge on output.</summary>
    public const double ThinkingMax = 1.5;

    /// <summary>Spoken words per second (Turkish).</summary>
    public const double TurkishWordsPerSecond = 2.3;

    /// <summary>Spoken words per second (English).</summary>
    public const double EnglishWordsPerSecond = 2.6;
}
