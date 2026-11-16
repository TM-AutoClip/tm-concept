namespace TMConcept.Core.Pronunciation;

/// <summary>
/// Units of measure, case-sensitive. <see cref="Anywhere"/> are read wherever they stand as a word (no Turkish word is spelled like them);
/// <see cref="AfterNumber"/> only right after a number ("5 m", "3 g"), because alone they are letters or Turkish words ("ha", "sa", "l").
/// <see cref="Speeds"/> are read "saatte yüz yirmi kilometre": the time first, as Turkish says it.
/// </summary>
public static class TurkishUnits
{
    /// <summary>Units read anywhere.</summary>
    public static readonly IReadOnlyDictionary<string, string> Anywhere = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["km²"] = "kilometrekare", ["m²"] = "metrekare", ["m³"] = "metreküp", ["cm²"] = "santimetrekare", ["cm³"] = "santimetreküp",
        ["km"] = "kilometre", ["cm"] = "santimetre", ["mm"] = "milimetre", ["µm"] = "mikrometre", ["μm"] = "mikrometre",
        ["kg"] = "kilogram", ["mg"] = "miligram", ["µg"] = "mikrogram", ["ml"] = "mililitre", ["mL"] = "mililitre",
        ["KB"] = "kilobayt", ["kB"] = "kilobayt", ["MB"] = "megabayt", ["GB"] = "gigabayt", ["TB"] = "terabayt", ["PB"] = "petabayt",
        ["Mbps"] = "megabit", ["Gbps"] = "gigabit", ["kHz"] = "kilohertz", ["MHz"] = "megahertz", ["GHz"] = "gigahertz",
        ["kWh"] = "kilovatsaat", ["kW"] = "kilovat", ["MW"] = "megavat", ["GW"] = "gigavat", ["mAh"] = "miliamper saat",
        ["kcal"] = "kilokalori", ["mmHg"] = "milimetre cıva", ["dB"] = "desibel",
    };

    /// <summary>Units read only after a number.</summary>
    public static readonly IReadOnlyDictionary<string, string> AfterNumber = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["m2"] = "metrekare", ["m3"] = "metreküp", ["km2"] = "kilometrekare", ["nm"] = "nanometre", ["m"] = "metre", ["gr"] = "gram",
        ["g"] = "gram", ["lt"] = "litre", ["l"] = "litre", ["L"] = "litre", ["cl"] = "santilitre", ["sn"] = "saniye", ["dk"] = "dakika",
        ["sa"] = "saat", ["ms"] = "milisaniye", ["ha"] = "hektar", ["Hz"] = "hertz", ["W"] = "vat", ["V"] = "volt", ["mV"] = "milivolt",
        ["kV"] = "kilovolt", ["mA"] = "miliamper", ["cal"] = "kalori", ["px"] = "piksel", ["MP"] = "megapiksel", ["mil"] = "mil",
        ["ft"] = "fit", ["mi"] = "mil", ["lb"] = "libre", ["oz"] = "ons", ["cc"] = "santimetreküp",
    };

    /// <summary>Speeds: the time word and the distance word.</summary>
    public static readonly IReadOnlyDictionary<string, (string Time, string Distance)> Speeds =
        new Dictionary<string, (string Time, string Distance)>(StringComparer.Ordinal)
        {
            ["km/sa"] = ("saatte", "kilometre"), ["km/s"] = ("saatte", "kilometre"), ["km/h"] = ("saatte", "kilometre"),
            ["m/sn"] = ("saniyede", "metre"), ["m/s"] = ("saniyede", "metre"), ["mph"] = ("saatte", "mil"),
        };
}
