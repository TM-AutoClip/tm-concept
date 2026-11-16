namespace TMConcept.Core.Pronunciation;

/// <summary>
/// Dotted Turkish abbreviations, case-sensitive, the longest first when they overlap. <see cref="Titles"/> stand before a name and never
/// end a sentence (Dr., Prof. …); <see cref="Trailing"/> may end one (vb., vs., yy. …), and then the reading keeps the full stop.
/// </summary>
public static class TurkishAbbreviations
{
    /// <summary>Titles before a name.</summary>
    public static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Prof. Dr."] = "profesör doktor", ["Doç. Dr."] = "doçent doktor", ["Yrd. Doç. Dr."] = "yardımcı doçent doktor",
        ["Dr. Öğr. Üyesi"] = "doktor öğretim üyesi", ["Öğr. Gör."] = "öğretim görevlisi", ["Arş. Gör."] = "araştırma görevlisi",
        ["Uzm. Dr."] = "uzman doktor", ["Op. Dr."] = "operatör doktor", ["Yrd. Doç."] = "yardımcı doçent", ["Prof."] = "profesör",
        ["Doç."] = "doçent", ["Dr."] = "doktor", ["Av."] = "avukat", ["Uzm."] = "uzman", ["Müh."] = "mühendis", ["Hz."] = "hazreti",
        ["Sn."] = "sayın", ["Org."] = "orgeneral", ["Korg."] = "korgeneral", ["Tümg."] = "tümgeneral", ["Tuğg."] = "tuğgeneral",
        ["Alb."] = "albay", ["Yzb."] = "yüzbaşı", ["Bnb."] = "binbaşı", ["Tğm."] = "teğmen", ["Ütğm."] = "üsteğmen",
        ["Bkz."] = "bakınız", ["bkz."] = "bakınız", ["Örn."] = "örneğin", ["örn."] = "örneğin",
    };

    /// <summary>Abbreviations that may end a sentence.</summary>
    public static readonly IReadOnlyDictionary<string, string> Trailing = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["yy."] = "yüzyıl", ["vb."] = "ve benzeri", ["vs."] = "vesaire", ["vd."] = "ve diğerleri", ["yak."] = "yaklaşık",
        ["ort."] = "ortalama", ["maks."] = "maksimum", ["min."] = "minimum", ["Ltd. Şti."] = "limitet şirketi", ["Ltd."] = "limitet",
        ["Şti."] = "şirketi", ["A.Ş."] = "anonim şirketi", ["T.C."] = "te ce", ["Cad."] = "caddesi", ["Sok."] = "sokağı",
        ["Mah."] = "mahallesi", ["Bulv."] = "bulvarı", ["Apt."] = "apartmanı", ["dk."] = "dakika", ["sn."] = "saniye", ["No."] = "numara",
    };
}
