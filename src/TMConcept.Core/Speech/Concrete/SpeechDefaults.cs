using TMConcept.Core.Models;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Speech;

/// <summary>
/// The default local voice of an output language (research of 7 Nov 2026): Kokoro for English (its best voices are English ones), Supertonic 3
/// for every other language (it speaks all of them). Used when a saved voice does not speak the profile's language any more (the language
/// was changed) and for the order of the suggested voices.
/// </summary>
public static class SpeechDefaults
{
    /// <summary>The engine code of the default voice of <paramref name="language"/>.</summary>
    public static string EngineFor(string language) => language == ContentLanguages.English ? SpeechEngines.Kokoro : SpeechEngines.Supertonic;

    /// <summary>The catalog key ("local/kokoro") of the default voice.</summary>
    public static string ModelFor(string language) => ModelProviders.Local + "/" + EngineFor(language);
}
