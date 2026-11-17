namespace TMConcept.Core.Research;

/// <summary>
/// Words of a subject (folded, see <see cref="Text.TurkishFold"/>; Turkish and English) that point at a language whose Wikipedia knows the
/// subject best: an Ottoman subject reads Turkish, a Byzantine one Greek, a Safavid one Persian. A word ending in "*" matches the start of a
/// subject word ("japon*" matches "japonya"), a word with a space matches those words in a row, any other word only the whole word ("cin" is
/// China, not "cinayet"; "roma" is Rome, while "roman" is a novel in Turkish). Deterministic: the same subject always reads the same
/// languages, in this table's order. The planner keeps at most <see cref="ResearchLimits.LanguagesMax"/> languages in all.
/// </summary>
public static class ResearchLanguageHints
{
    /// <summary>Language and its words, in order.</summary>
    public static readonly IReadOnlyList<(string Language, string[] Words)> Table =
    [
        ("tr", ["osmanli*", "ottoman*", "selcuklu*", "seljuk*", "anadolu*", "anatolia*", "ataturk*"]),
        ("el", ["bizans*", "byzant*", "yunan*", "greek", "greece", "atina", "athens", "sparta", "konstantinopolis", "constantinople"]),
        ("it", ["roma", "romali*", "roman empire", "roman republic", "ancient rome", "rome", "italya*", "italy", "italian", "venedik*", "venice", "venetian",
            "floransa*", "florence", "ronesans*", "renaissance", "ceneviz*", "genoa"]),
        ("fr", ["fransa*", "france", "french", "fransiz*", "napolyon*", "napoleon*", "galya*", "gaul"]),
        ("de", ["almanya*", "german*", "alman", "almanlar*", "prusya*", "prussia*", "habsburg*", "bavyera*", "bavaria*"]),
        ("es", ["ispanya*", "spain", "spanish", "endulus*", "andalus*", "kastilya*", "castile", "aztek*", "aztec*", "inka*", "inca*", "conquista*"]),
        ("ru", ["rus", "ruslar*", "rusya*", "russia*", "sovyet*", "soviet*", "carlik*", "tsar*", "moskova*", "moscow"]),
        ("ar", ["arap*", "arab", "arabs", "arabic", "arabian","abbasi*", "abbasid*", "emevi*", "umayyad*", "misir*", "egypt*", "memluk*", "mamluk*", "fatimi*", "fatimid*"]),
        ("fa", ["iran*", "persia*", "safevi*", "safavid*", "sasani*", "sassanid*", "ahameni*", "achaemenid*", "kacar*", "qajar*"]),
        ("ja", ["japon*", "japan*", "samuray*", "samurai*", "sogun*", "shogun*", "meiji"]),
        ("zh", ["cin", "cinli*", "china", "chinese", "ming hanedani", "ming dynasty", "qing", "konfucyus*", "confucius"]),
        ("ko", ["kore", "koreli*", "korea*", "joseon", "goryeo"]),
        ("pt", ["portekiz*", "portugal", "portuguese", "brezilya*", "brazil*"]),
        ("pl", ["polonya*", "poland", "polish", "leh", "lehistan*"]),
        ("hu", ["macar*", "hungary", "hungarian"]),
        ("nl", ["hollanda*", "netherlands", "dutch"]),
        ("sv", ["isvec*", "sweden", "swedish", "viking*"]),
        ("hy", ["ermeni*", "armenia*"]),
        ("ka", ["gurcu*", "gurcistan*", "georgian"]),
        ("he", ["israil*", "israel*", "yahudi*", "jewish", "kudus*", "jerusalem"]),
    ];
}
