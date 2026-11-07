using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;

namespace TMConcept.Core.Profiles;

/// <summary>
/// The output languages videos can be made in (concept, narration, subtitles and on-screen text are written in it; the user's notes may be
/// in the interface language). Chosen from what the local voice Supertonic 3 speaks (31 languages) and what the chat models write well, with
/// a Latin or Cyrillic script the subtitle font (DejaVu Sans) draws and words separated by spaces (the subtitle and word duplicate
/// rules split at spaces). Japanese, Chinese, Korean, Arabic and Hindi are left out for that reason. A new language = one entry here (and a
/// voice that speaks it); the prompts stay Turkish or English (<see cref="OutputLanguage.PromptLanguage"/>).
/// </summary>
public static class ContentLanguages
{
    /// <summary>Turkish.</summary>
    public const string Turkish = "tr";

    /// <summary>English.</summary>
    public const string English = "en";

    /// <summary>German.</summary>
    public const string German = "de";

    /// <summary>Spanish.</summary>
    public const string Spanish = "es";

    /// <summary>French.</summary>
    public const string French = "fr";

    /// <summary>Italian.</summary>
    public const string Italian = "it";

    /// <summary>Portuguese.</summary>
    public const string Portuguese = "pt";

    /// <summary>Dutch.</summary>
    public const string Dutch = "nl";

    /// <summary>Polish.</summary>
    public const string Polish = "pl";

    /// <summary>Russian.</summary>
    public const string Russian = "ru";

    /// <summary>Swedish.</summary>
    public const string Swedish = "sv";

    /// <summary>Indonesian.</summary>
    public const string Indonesian = "id";

    /// <summary>English filler words; every language's word check leaves these out too (names and loan words often come in English).</summary>
    public static readonly IReadOnlyList<string> EnglishStopWords =
        ["the", "a", "an", "and", "or", "of", "in", "on", "to", "for", "with", "by", "is", "was", "how", "why", "what", "who", "part", "episode"];

    /// <summary>
    /// Every output language, in the order of the profile page. Turkish and English keep the rates the estimates used before (2.3 / 2.6 words
    /// per second; 7.5 / 6.0 characters per word). The others: German and Swedish have long compound words (fewer, longer words per second),
    /// Polish, Russian and Indonesian long multi-syllable words; Spanish, French and Italian are spoken fast with short words.
    /// </summary>
    public static readonly IReadOnlyList<OutputLanguage> Definitions =
    [
        new(Turkish, "Türkçe", "Türkçe", "Turkish", NarrationRate.TurkishWordsPerSecond, NarrationRate.TurkishCharactersPerWord,
            TokenRules.TurkishCharactersPerToken, 2, "bölüm",
            ["ve", "ile", "bir", "bu", "su", "o", "da", "de", "mi", "mu", "icin", "gibi", "en", "ne", "nasil", "neden", "kim", "hangi", "olan", "oldu", "cok",
                "daha", "ki", "ya", "veya", "ama", "her", "hakkinda", "nedir", "kimdi", "bolum", "kisim", "seri"],
            "Merhaba! Kanalının anlatımı böyle duyulacak."),
        new(English, "English", "İngilizce", "English", NarrationRate.EnglishWordsPerSecond, NarrationRate.EnglishCharactersPerWord,
            TokenRules.EnglishCharactersPerToken, 1, "Part", [], "Hello! This is how your channel's narration will sound."),
        new(German, "Deutsch", "Almanca", "German", 2.2, 7.0, 3.0, 1.5, "Teil",
            ["der", "die", "das", "und", "oder", "ein", "eine", "einer", "eines", "im", "am", "an", "auf", "mit", "von", "zu", "fur", "ist", "war", "wie",
                "warum", "was", "wer", "den", "dem", "des", "teil", "folge"],
            "Hallo! So wird die Erzählstimme deines Kanals klingen."),
        new(Spanish, "Español", "İspanyolca", "Spanish", 2.6, 5.9, 3.2, 1.3, "Parte",
            ["el", "la", "los", "las", "y", "o", "un", "una", "de", "del", "en", "con", "por", "para", "es", "fue", "como", "que", "quien", "al", "parte",
                "capitulo", "episodio"],
            "¡Hola! Así sonará la narración de tu canal."),
        new(French, "Français", "Fransızca", "French", 2.6, 5.8, 3.2, 1.3, "Partie",
            ["le", "la", "les", "et", "ou", "un", "une", "de", "du", "des", "en", "au", "aux", "avec", "pour", "par", "est", "etait", "comment", "pourquoi",
                "qui", "quoi", "partie", "episode"],
            "Bonjour ! Voici comment sonnera la narration de ta chaîne."),
        new(Italian, "Italiano", "İtalyanca", "Italian", 2.5, 6.0, 3.0, 1.4, "Parte",
            ["il", "lo", "la", "i", "gli", "le", "e", "o", "un", "una", "di", "del", "della", "dei", "in", "con", "per", "da", "era", "come", "perche", "chi",
                "parte", "puntata", "episodio"],
            "Ciao! Ecco come suonerà la narrazione del tuo canale."),
        new(Portuguese, "Português", "Portekizce", "Portuguese", 2.5, 5.9, 3.1, 1.4, "Parte",
            ["o", "a", "os", "as", "e", "ou", "um", "uma", "de", "do", "da", "dos", "das", "em", "no", "na", "com", "por", "para", "foi", "como", "que",
                "quem", "parte", "episodio"],
            "Olá! É assim que a narração do seu canal vai soar."),
        new(Dutch, "Nederlands", "Felemenkçe", "Dutch", 2.4, 6.3, 3.0, 1.5, "Deel",
            ["de", "het", "een", "en", "of", "van", "in", "op", "met", "voor", "door", "is", "was", "hoe", "waarom", "wat", "wie", "deel", "aflevering"],
            "Hallo! Zo klinkt de vertelstem van je kanaal."),
        new(Polish, "Polski", "Lehçe", "Polish", 2.1, 7.0, 2.4, 2, "Część",
            ["i", "w", "z", "na", "do", "o", "od", "za", "jak", "dlaczego", "co", "kto", "jest", "byl", "czesc", "odcinek"],
            "Cześć! Tak będzie brzmiała narracja twojego kanału."),
        new(Russian, "Русский", "Rusça", "Russian", 2.1, 7.2, 2.4, 2, "Часть",
            ["и", "в", "на", "с", "о", "по", "к", "из", "как", "почему", "что", "кто", "это", "был", "часть", "серия"],
            "Привет! Так будет звучать озвучка твоего канала."),
        new(Swedish, "Svenska", "İsveççe", "Swedish", 2.3, 6.3, 2.9, 1.5, "Avsnitt",
            ["och", "eller", "en", "ett", "i", "pa", "med", "av", "for", "till", "ar", "var", "hur", "varfor", "vad", "vem", "avsnitt"],
            "Hej! Så här kommer berättarrösten i din kanal att låta."),
        new(Indonesian, "Bahasa Indonesia", "Endonezce", "Indonesian", 2.2, 6.8, 3.2, 1.4, "Bagian",
            ["dan", "atau", "yang", "di", "ke", "dari", "dengan", "untuk", "adalah", "bagaimana", "mengapa", "apa", "siapa", "bagian"],
            "Halo! Beginilah narasi kanalmu akan terdengar."),
    ];

    /// <summary>Every output language code.</summary>
    public static readonly IReadOnlyList<string> All = Definitions.Select(d => d.Code).ToList();
}
