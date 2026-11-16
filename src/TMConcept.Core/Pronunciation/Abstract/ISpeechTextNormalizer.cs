namespace TMConcept.Core.Pronunciation;

/// <summary>
/// Turns written text of one language into the words a voice should say (numbers, dates, abbreviations, units, symbols), after the
/// pronunciation dictionary and before any voice, local or online. Only the spoken text changes; subtitles keep what was written.
/// </summary>
public interface ISpeechTextNormalizer
{
    /// <summary>Whether it handles text of <paramref name="language"/> (an output language code, "tr").</summary>
    bool Supports(string language);

    /// <summary>The spoken form of <paramref name="text"/>.</summary>
    string Normalize(string text);

    /// <summary>
    /// A dictionary word's spoken form with the suffix written after its apostrophe, agreed with how it is spoken ("Gugıl" + "ın" →
    /// "Gugılın" in Turkish).
    /// </summary>
    string Join(string spoken, string suffix);
}
