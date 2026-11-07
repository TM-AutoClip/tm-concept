using System.Globalization;

namespace TMConcept.Core.Profiles;

/// <summary>
/// A language videos can be made in (the profile's output language; the interface is Turkish or English, the user's notes may be in either).
/// The rates are rough rules of thumb for estimates before anything is spoken (narration pace of a documentary voice-over, read from
/// published words-per-minute ranges; not measured by us) and are corrected by the measured voice once a scene is spoken.
/// </summary>
/// <param name="Code">ISO 639-1 code ("de"); also what the voices are told.</param>
/// <param name="Native">The language's own name ("Deutsch").</param>
/// <param name="Turkish">Its name in Turkish ("Almanca").</param>
/// <param name="English">Its name in English ("German"); the prompts name the output language with it.</param>
/// <param name="WordsPerSecond">Narration words per second.</param>
/// <param name="CharactersPerWord">Characters per narration word, the space included.</param>
/// <param name="CharactersPerToken">Characters per token of a chat model (newer tokenizers split non-English text into more tokens).</param>
/// <param name="EmbeddingTokenFactor">Embedding tokens of a title and summary compared with English.</param>
/// <param name="PartWord">The word of a series part in titles ("Teil" in "… — Teil 3").</param>
/// <param name="StopWords">Filler words the word duplicate check leaves out, folded (lower case, without diacritics).</param>
/// <param name="Sample">The sentence the "Listen" button speaks.</param>
public sealed record OutputLanguage(string Code, string Native, string Turkish, string English, double WordsPerSecond, double CharactersPerWord,
    double CharactersPerToken, double EmbeddingTokenFactor, string PartWord, IReadOnlyList<string> StopWords, string Sample)
{
    /// <summary>The culture of the language (its upper and lower case: Turkish "I" / "ı" and "İ" / "i").</summary>
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Code);

    /// <summary>The language of the model prompts for this output language: Turkish for Turkish videos, English for every other one.</summary>
    public string PromptLanguage => Code == ContentLanguages.Turkish ? ContentLanguages.Turkish : ContentLanguages.English;
}
