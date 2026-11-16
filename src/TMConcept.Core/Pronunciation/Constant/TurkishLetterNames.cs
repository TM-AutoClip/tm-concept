namespace TMConcept.Core.Pronunciation;

/// <summary>
/// How Turkish reads the letters of an abbreviation spelled letter by letter ("TBMM" → "te be me me", "THY" → "te ha ye": H and K as in
/// everyday speech, "ha" and "ka"), and the short Turkish words of all-capital headlines that are words, not abbreviations ("VE", "BU").
/// </summary>
public static class TurkishLetterNames
{
    /// <summary>Letter → its name.</summary>
    public static readonly IReadOnlyDictionary<char, string> Names = new Dictionary<char, string>
    {
        ['A'] = "a", ['B'] = "be", ['C'] = "ce", ['Ç'] = "çe", ['D'] = "de", ['E'] = "e", ['F'] = "fe", ['G'] = "ge", ['Ğ'] = "yumuşak ge",
        ['H'] = "ha", ['I'] = "ı", ['İ'] = "i", ['J'] = "je", ['K'] = "ka", ['L'] = "le", ['M'] = "me", ['N'] = "ne", ['O'] = "o",
        ['Ö'] = "ö", ['P'] = "pe", ['Q'] = "kü", ['R'] = "re", ['S'] = "se", ['Ş'] = "şe", ['T'] = "te", ['U'] = "u", ['Ü'] = "ü",
        ['V'] = "ve", ['W'] = "çift ve", ['X'] = "iks", ['Y'] = "ye", ['Z'] = "ze", ['Â'] = "a", ['Î'] = "i", ['Û'] = "u",
    };

    /// <summary>Turkish words written in capitals in headlines: read as words, never spelled.</summary>
    public static readonly IReadOnlySet<string> CapitalWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "VE", "DE", "DA", "BU", "ŞU", "Mİ", "MI", "MU", "MÜ", "NE", "EN", "İLE", "Kİ", "YA", "AMA", "BİR", "İKİ", "ÜÇ", "SON", "İLK", "HER",
        "HİÇ", "YOK", "VAR", "ÇOK", "AZ", "İÇ", "DIŞ", "BEN", "SEN", "BİZ", "SİZ", "ŞOK", "ÖN", "AÇ", "AT", "AL", "VER", "GEL", "GİT", "OL",
        "YAP", "UN", "SU", "ON", "AY", "YIL", "GÜN", "İŞ", "EV", "EL", "AN", "ÖZ", "O", "YENİ", "TÜM", "HEM", "GİBİ", "İÇİN",
    };
}
