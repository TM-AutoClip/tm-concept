namespace TMConcept.Core.Concepts;

/// <summary>Estimated speaking rate (words per second) used for the narration length of a concept; corrected later by the measured voice.</summary>
public static class NarrationRate
{
    /// <summary>
    /// Turkish: 1.8 words (about 13.5 characters) per second, the pauses between sentences and scenes included (0.1.31). Until then 2.3
    /// (17.25 characters per second): the concepts were written for a voice that never breathes, and the scripts came out over-packed.
    /// </summary>
    public const double TurkishWordsPerSecond = 1.8;

    /// <summary>English.</summary>
    public const double EnglishWordsPerSecond = 2.6;

    /// <summary>Characters per Turkish narration word, the space included (Turkish words are long: agglutination).</summary>
    public const double TurkishCharactersPerWord = 7.5;

    /// <summary>Characters per English narration word, the space included.</summary>
    public const double EnglishCharactersPerWord = 6.0;

    /// <summary>Scene lines ([on screen: …]) on top of the narration, as a share of it.</summary>
    public const double SceneLineShare = 0.3;

    /// <summary>Characters of a concept besides its script: title, two-sentence summary, JSON keys and escapes.</summary>
    public const int ConceptOverheadCharacters = 500;
}
