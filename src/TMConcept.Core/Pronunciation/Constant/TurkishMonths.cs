namespace TMConcept.Core.Pronunciation;

/// <summary>Turkish month names for dates read aloud ("14.10.2026" → "on dört Ekim iki bin yirmi altı").</summary>
public static class TurkishMonths
{
    /// <summary>Month names (index 1 = January, index 0 empty).</summary>
    public static readonly IReadOnlyList<string> Names =
        ["", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];

    /// <summary>The names as a regular expression alternation.</summary>
    public const string Pattern = "Ocak|Şubat|Mart|Nisan|Mayıs|Haziran|Temmuz|Ağustos|Eylül|Ekim|Kasım|Aralık";
}
