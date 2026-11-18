using System.Globalization;
using System.Text.RegularExpressions;
using TMConcept.Core.Profiles;
using TMConcept.Core.Text;

namespace TMConcept.Core.Pronunciation;

/// <summary>
/// Written Turkish to the words a voice says, after the pronunciation dictionary. In order:
/// <list type="number">
/// <item>eras: "MÖ 330" → "milattan önce 330", "M.S." → "milattan sonra" ("MS" only before a year or a Roman numeral);</item>
/// <item>dates and times: "14.10.2026" and "14/10/2026" → "on dört Ekim iki bin yirmi altı", "2026-10-14" too; "09:30'da" and "saat 14.30"
/// → "dokuz otuzda", "on dört otuz";</item>
/// <item>speeds ("120 km/sa" → "saatte 120 kilometre") and symbols between numbers (+ artı, = eşittir, × and x çarpı, / bölü);</item>
/// <item>Roman ordinals: "II. Mehmed" → "İkinci Mehmed", "XV. yüzyıl" → "on beşinci yüzyıl" (one letter only I, V or X, never L/C/D/M:
/// initials);</item>
/// <item>ordinals: "3. sırada" → "üçüncü sırada", "1. Dünya Savaşı" → "Birinci Dünya Savaşı", "3.'sü" → "üçüncüsü" (above 50 before a
/// capital letter it is the end of a sentence);</item>
/// <item>numbers: thousands dots, decimal comma ("virgül") or dot ("nokta"), ranges ("1914-1918" → "… on dört ile … on sekiz"), "eksi",
/// percent ("%40" → "yüzde kırk"), currencies (₺, TL, $, €, £, ¥, ₽ and codes; "12,50 TL" → "on iki lira elli kuruş"), suffixes agreed
/// with the reading ("1453'te" → "… elli üçte", "4'ü" → "dördü"), ordinal suffixes ("2'nci" → "ikinci"); units glued to a number are
/// split first ("25km");</item>
/// <item>dotted abbreviations ("Prof. Dr." → "profesör doktor", "vb." → "ve benzeri"), units ("5 kg" → "beş kilogram"; m, g, l … only
/// after a number), degrees ("25°C" → "yirmi beş derece");</item>
/// <item>left-over symbols (&amp; ve, @ et, % yüzde) and digits (inside words: "MP3" → "MP üç");</item>
/// <item>capital abbreviations: currency codes as words, Turkish headline words ("VE" → "ve"), Roman numerals of I/V/X ("XIV" → "on
/// dördüncü"), a pronounceable word of four or more letters read as a word ("DİKKAT" → "Dikkat"), anything else spelled ("TBMM'de" →
/// "te be me mede"). Abbreviations read in their own way ("NATO", "!IT") are in the dictionary, which runs first.</item>
/// </list>
/// A suffix after an apostrophe is joined to the reading and agreed with it (<see cref="TurkishHarmony"/>); the apostrophe goes.
/// Before every step the text is put in composed form and the circumflex vowels lose their mark (<see cref="PlainVowels"/>: "hikâye" →
/// "hikaye", 0.1.24: the voices misread them), and dashes and typographic quotes become what every voice knows (<see cref="Typography"/>).
/// </summary>
public sealed partial class TurkishSpeechTextNormalizer : SpeechTextNormalizerBase
{
    private static readonly IReadOnlyDictionary<string, string> Currencies = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TL"] = "lira", ["TRY"] = "lira", ["₺"] = "lira", ["$"] = "dolar", ["USD"] = "dolar", ["€"] = "avro", ["EUR"] = "avro",
        ["£"] = "sterlin", ["GBP"] = "sterlin", ["¥"] = "yen", ["JPY"] = "yen", ["₽"] = "ruble", ["RUB"] = "ruble", ["CHF"] = "İsviçre frangı",
    };

    private static readonly IReadOnlyDictionary<string, string> Subunits = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["lira"] = "kuruş", ["dolar"] = "sent", ["avro"] = "sent", ["sterlin"] = "peni",
    };

    private static readonly Lazy<Regex> GluedUnit = new(() => new Regex(
        "(?<=\\d)(?=(?:" + Alternation(TurkishUnits.Anywhere.Keys.Concat(TurkishUnits.AfterNumber.Keys).Concat(TurkishUnits.Speeds.Keys))
        + ")(?![\\p{L}\\p{N}]))", RegexOptions.CultureInvariant));

    private static readonly Lazy<Regex> Speed = new(() => new Regex(
        "(?<![\\p{L}\\p{N}.,])(?<n>\\d+(?:[.,]\\d+)?)[ ]?(?<u>" + Alternation(TurkishUnits.Speeds.Keys) + ")(?:['’](?<suf>\\p{L}+))?(?![\\p{L}\\p{N}])",
        RegexOptions.CultureInvariant));

    private static readonly Lazy<Regex> Dotted = new(() => new Regex(
        "(?<![\\p{L}\\p{N}])(?<k>" + Alternation(TurkishAbbreviations.Titles.Keys.Concat(TurkishAbbreviations.Trailing.Keys)) + ")(?:['’](?<suf>\\p{L}+))?",
        RegexOptions.CultureInvariant));

    private static readonly Lazy<Regex> AnywhereUnit = new(() => new Regex(
        "(?<![\\p{L}\\p{N}])(?<u>" + Alternation(TurkishUnits.Anywhere.Keys) + ")(?:['’](?<suf>\\p{L}+))?(?![\\p{L}\\p{N}])",
        RegexOptions.CultureInvariant));

    private static readonly Lazy<Regex> NumberUnit = new(() => new Regex(
        "(?<=(?:^|[^\\p{L}])(?:" + Alternation(TurkishNumbers.Words) + ")[ ]?)(?<u>" + Alternation(TurkishUnits.AfterNumber.Keys)
        + ")(?:['’](?<suf>\\p{L}+))?(?![\\p{L}\\p{N}])", RegexOptions.CultureInvariant));

    private static readonly Lazy<Regex> NumberCurrency = new(() => new Regex(
        "(?<=(?:^|[^\\p{L}])(?:" + Alternation(TurkishNumbers.Words) + ")[ ]?)(?<c>[$€₺£¥₽])(?:['’](?<suf>\\p{L}+))?", RegexOptions.CultureInvariant));

    /// <inheritdoc />
    protected override string Language => ContentLanguages.Turkish;

    /// <inheritdoc />
    public override string Join(string spoken, string suffix) => TurkishHarmony.Attach(spoken, suffix ?? string.Empty);

    /// <inheritdoc />
    protected override string Steps(string text)
    {
        text = Typography(PlainVowels(text)).Replace('’', '\'');
        text = Replace(text, EraBefore(), (m, t) => SpaceAfter(t, m.Index + m.Length, "milattan önce"));
        text = Replace(text, EraAfter(), (m, t) => SpaceAfter(t, m.Index + m.Length, "milattan sonra"));
        text = Replace(text, NumericDate(), Date);
        text = Replace(text, IsoDate(), Date);
        text = Replace(text, Time(), ReadTime);
        text = GluedUnit.Value.Replace(text, " ");
        text = Replace(text, Speed.Value, ReadSpeed);
        text = Replace(text, CurrencyBeforeScale(), (m, _) => m.Groups["n"].Value + " " + m.Groups["s"].Value + " " + m.Groups["c"].Value);
        text = Replace(text, BetweenNumbers(), (m, _) => m.Groups["s"].Value switch
        {
            "+" => " artı ",
            "=" => " eşittir ",
            "/" => " bölü ",
            _ => " çarpı ",
        });
        text = Replace(text, Roman(), ReadRoman);
        text = Replace(text, Ordinal(), ReadOrdinal);
        text = Replace(text, Number(), ReadNumber);
        text = Replace(text, Dotted.Value, ReadDotted);
        text = Replace(text, AnywhereUnit.Value, (m, t) => Unit(m, t, TurkishUnits.Anywhere));
        text = Replace(text, NumberUnit.Value, (m, t) => Unit(m, t, TurkishUnits.AfterNumber));
        text = Replace(text, Degree(), ReadDegree);
        text = Replace(text, NumberCurrency.Value, (m, t) => Around(t, m, Suffixed(Currencies[m.Groups["c"].Value], m.Groups["suf"])));
        text = Replace(text, Symbol(), (m, t) => Around(t, m, m.Value switch
        {
            "&" => "ve",
            "@" => "et",
            _ => "yüzde",
        }));
        text = Replace(text, Digits(), (m, t) => Around(t, m, TurkishNumbers.Cardinal(m.Value)));
        text = Replace(text, Capitals(), ReadCapitals);
        return Spaces().Replace(text, " ").Trim();
    }

    /// <summary>
    /// The text in composed form with the circumflex vowels plain (â → a, î → i, û → u; Â → A, Î → İ, Û → U). The voices read them badly:
    /// Supertonic splits every letter into its base and mark (its vocabulary has the combining circumflex, which Turkish training text
    /// hardly ever holds, so "hâlâ" and "hikâye" come out wrong), Ahenk Cep drops the mark itself. The plain vowel is what a listener
    /// expects; the subtitles keep the written spelling.
    /// </summary>
    public static string PlainVowels(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var composed = text.Normalize(System.Text.NormalizationForm.FormC);
        if (composed.AsSpan().IndexOfAny("âîûÂÎÛ") < 0)
        {
            return composed;
        }

        return string.Create(composed.Length, composed, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = source[i] switch
                {
                    'â' => 'a',
                    'î' => 'i',
                    'û' => 'u',
                    'Â' => 'A',
                    'Î' => 'İ',
                    'Û' => 'U',
                    var c => c,
                };
            }
        });
    }

    /// <summary>
    /// Dashes and typographic marks every voice knows: a dash between words (em or en dash, not a range of numbers) is a short pause (a
    /// comma), the ellipsis character is three dots, curly double quotes are plain ones. Supertonic has none of them in its vocabulary.
    /// </summary>
    public static string Typography(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = WordDash().Replace(text, ", ");
        return text.Replace("…", "...", StringComparison.Ordinal).Replace('“', '"').Replace('”', '"').Replace('„', '"');
    }

    /// <summary>
    /// Whether an all-capital word can be read as a word: four or more letters, a vowel, no three vowels in a row, and no three consonants
    /// in a row in a four-letter word (four in a longer one).
    /// </summary>
    public static bool IsPronounceable(string capitals)
    {
        ArgumentNullException.ThrowIfNull(capitals);
        if (capitals.Length < 4)
        {
            return false;
        }

        var lower = TurkishLetters.Lower(capitals);
        var limit = lower.Length == 4 ? 3 : 4;
        int consonants = 0, vowels = 0, total = 0;
        foreach (var c in lower)
        {
            if (TurkishLetters.IsVowel(c))
            {
                total++;
                vowels++;
                consonants = 0;
            }
            else
            {
                consonants++;
                vowels = 0;
            }

            if (consonants >= limit || vowels >= 3)
            {
                return false;
            }
        }

        return total > 0;
    }

    /// <summary>The letters of an abbreviation by their Turkish names ("TBMM" → "te be me me").</summary>
    public static string Spell(string capitals)
    {
        ArgumentNullException.ThrowIfNull(capitals);
        return string.Join(' ', capitals.Select(c => TurkishLetterNames.Names.TryGetValue(c, out var name) ? name : c.ToString()));
    }

    private static string? Date(Match m, string text)
    {
        var day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        if (day is < 1 or > 31 || month is < 1 or > 12)
        {
            return null;
        }

        var reading = TurkishNumbers.Cardinal(day) + " " + TurkishMonths.Names[month] + " " + TurkishNumbers.Cardinal(m.Groups["y"].Value);
        return SpaceAfter(text, m.Index + m.Length, Suffixed(reading, m.Groups["suf"]));
    }

    private static string ReadTime(Match m, string text)
    {
        var hour = TurkishNumbers.Cardinal(int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture));
        var minute = m.Groups["mi"].Value;
        var reading = minute == "00" ? hour : hour + " " + TurkishNumbers.Cardinal(minute);
        return Around(text, m, Suffixed(reading, m.Groups["suf"]));
    }

    private static string ReadSpeed(Match m, string text)
    {
        var (time, distance) = TurkishUnits.Speeds[m.Groups["u"].Value];
        return Around(text, m, time + " " + m.Groups["n"].Value + " " + Suffixed(distance, m.Groups["suf"]));
    }

    private static string? ReadRoman(Match m, string text)
    {
        var roman = m.Groups["r"].Value;
        if ((roman.Length == 1 && roman[0] is not ('I' or 'V' or 'X')) || !RomanNumerals.TryParse(roman, out var value))
        {
            return null;
        }

        var ordinal = Suffixed(TurkishNumbers.Ordinal(value), m.Groups["suf"]);
        return NextWordIsCapital(text, m.Index + m.Length) ? TurkishLetters.Capitalize(ordinal) : ordinal;
    }

    private static string? ReadOrdinal(Match m, string text)
    {
        var value = long.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        var suffix = m.Groups["suf"];
        var capital = !suffix.Success && NextWordIsCapital(text, m.Index + m.Length);
        if (capital && value > 50)
        {
            return null;
        }

        var ordinal = Suffixed(TurkishNumbers.Ordinal(value), suffix);
        return capital ? TurkishLetters.Capitalize(ordinal) : ordinal;
    }

    private static string ReadNumber(Match m, string text)
    {
        var currency = m.Groups["curpre"].Success ? m.Groups["curpre"].Value : m.Groups["curpost"].Value;
        var word = currency.Length > 0 ? Currencies[currency] : null;
        var integer = m.Groups["int"].Value.Replace(".", string.Empty, StringComparison.Ordinal);
        var fraction = m.Groups["frac"];
        string reading;
        if (word is not null && fraction.Success && fraction.Value.Length == 2 && Subunits.TryGetValue(word, out var subunit) && !m.Groups["int2"].Success)
        {
            reading = TurkishNumbers.Cardinal(integer) + " " + word + (fraction.Value == "00" ? string.Empty : " " + TurkishNumbers.Cardinal(fraction.Value) + " " + subunit);
            word = null;
        }
        else
        {
            reading = Amount(integer, fraction, m.Groups["dot"]);
        }

        if (m.Groups["int2"].Success)
        {
            reading += " ile " + Amount(m.Groups["int2"].Value.Replace(".", string.Empty, StringComparison.Ordinal), m.Groups["frac2"], null);
        }
        else if (m.Groups["sign"].Success)
        {
            reading = "eksi " + reading;
        }

        var percent = m.Groups["pctpre"].Success || m.Groups["pctpost"].Success;
        if (percent)
        {
            reading = "yüzde " + reading;
        }

        if (word is not null)
        {
            reading += " " + word;
        }

        var suffix = m.Groups["suf"];
        if (suffix.Success && !percent && currency.Length == 0 && OrdinalSuffix().Match(TurkishLetters.Lower(suffix.Value)) is { Success: true } ordinal)
        {
            return SpaceAfter(text, m.Index + m.Length, TurkishHarmony.Attach(TurkishNumbers.OrdinalOf(reading), ordinal.Groups["rest"].Value));
        }

        return SpaceAfter(text, m.Index + m.Length, Suffixed(reading, suffix));
    }

    private static string Amount(string integer, Group fraction, Group? dot)
    {
        if (fraction.Success)
        {
            return TurkishNumbers.Decimal(integer, fraction.Value, "virgül");
        }

        return dot is { Success: true } ? TurkishNumbers.Decimal(integer, dot.Value, "nokta") : TurkishNumbers.Cardinal(integer);
    }

    private static string ReadDotted(Match m, string text)
    {
        var key = m.Groups["k"].Value;
        var title = TurkishAbbreviations.Titles.TryGetValue(key, out var reading);
        reading ??= TurkishAbbreviations.Trailing[key];
        var suffix = m.Groups["suf"];
        var end = m.Index + m.Length;
        if (suffix.Success)
        {
            return SpaceAfter(text, end, TurkishHarmony.Attach(reading, suffix.Value));
        }

        return !title && SentenceEndsAt(text, end) ? reading + "." : SpaceAfter(text, end, reading);
    }

    private static string Unit(Match m, string text, IReadOnlyDictionary<string, string> table) =>
        Around(text, m, Suffixed(table[m.Groups["u"].Value], m.Groups["suf"]));

    private static string ReadDegree(Match m, string text) =>
        Around(text, m, Suffixed(m.Groups["s"].Value == "F" ? "derece fahrenhayt" : "derece", m.Groups["suf"]));

    private static string ReadCapitals(Match m, string text)
    {
        var word = m.Groups["w"].Value;
        string reading;
        if (Currencies.TryGetValue(word, out var currency))
        {
            reading = currency;
        }
        else if (TurkishLetterNames.CapitalWords.Contains(word))
        {
            reading = TurkishLetters.Lower(word);
        }
        else if (word.Length > 1 && word.All(c => c is 'I' or 'V' or 'X') && RomanNumerals.TryParse(word, out var value))
        {
            reading = TurkishNumbers.Ordinal(value);
        }
        else
        {
            reading = IsPronounceable(word) ? TurkishLetters.Title(word) : Spell(word);
        }

        return Suffixed(reading, m.Groups["suf"]);
    }

    private static string Suffixed(string reading, Group? suffix) =>
        suffix is { Success: true } ? TurkishHarmony.Attach(reading, suffix.Value) : reading;

    private static string Around(string text, Match m, string reading) => SpaceAfter(text, m.Index + m.Length, SpaceBefore(text, m.Index, reading));

    private static string Alternation(IEnumerable<string> keys) =>
        string.Join('|', keys.Distinct(StringComparer.Ordinal).OrderByDescending(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).Select(Regex.Escape));

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:M\.[ ]?Ö\.?|MÖ)(?![\p{L}])", RegexOptions.CultureInvariant)]
    private static partial Regex EraBefore();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:M\.[ ]?S\.|MS(?=[ ]*(?:\d|[IVXLCDM]+\.)))(?![\p{L}])", RegexOptions.CultureInvariant)]
    private static partial Regex EraAfter();

    [GeneratedRegex(@"(?<![\p{N}.,/])(?<d>\d{1,2})(?<sep>[./])(?<m>\d{1,2})\k<sep>(?<y>\d{4})(?:'(?<suf>\p{L}+))?(?![\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"(?<![\p{N}.,/-])(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})(?:'(?<suf>\p{L}+))?(?![\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"(?:(?<![\p{N}:.,])(?<h>[01]?\d|2[0-3]):(?<mi>[0-5]\d)|(?<=\b[Ss]aat[ ]+)(?<h>[01]?\d|2[0-3])\.(?<mi>[0-5]\d))(?:'(?<suf>\p{L}+))?(?![\p{N}:])", RegexOptions.CultureInvariant)]
    private static partial Regex Time();

    [GeneratedRegex(@"(?<c>[$€₺£¥₽])[ ]?(?<n>\d[\d.,]*)[ ]+(?<s>bin|milyon|milyar|trilyon)(?![\p{L}])", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyBeforeScale();

    [GeneratedRegex(@"(?<=\d)[ ]?(?<s>[+=×x*/])[ ]?(?=\d)", RegexOptions.CultureInvariant)]
    private static partial Regex BetweenNumbers();

    [GeneratedRegex(@"(?<![\p{L}\p{N}.])(?<r>[IVXLCDM]+)(?:\.(?:'(?<suf>\p{L}+))?(?=\s+[\p{L}\d])|(?=\s+(?:yüzyıl|yy\.|asır|binyıl)))", RegexOptions.CultureInvariant)]
    private static partial Regex Roman();

    [GeneratedRegex(@"(?<![\p{L}\p{N}.,])(?<n>\d{1,9})\.(?:'(?<suf>\p{L}+)|(?=[ \t]+\p{L}))", RegexOptions.CultureInvariant)]
    private static partial Regex Ordinal();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_.,])(?:(?<curpre>[$€₺£¥₽])[ ]?)?(?<pctpre>%[ ]?)?(?<sign>[-−](?=\d))?" +
        @"(?<int>\d{1,3}(?:\.\d{3})+(?!\d|\.\d)|\d+)(?:,(?<frac>\d+)|\.(?<dot>\d+)(?!\d|\.\d))?" +
        @"(?:[ ]?[-–][ ]?(?<int2>\d{1,3}(?:\.\d{3})+(?!\d|\.\d)|\d+)(?:,(?<frac2>\d+))?)?" +
        @"(?<pctpost>[ ]?%)?(?:[ ]?(?<curpost>[$€₺£¥₽]|TL|TRY|USD|EUR|GBP|JPY|RUB|CHF)(?![\p{L}]))?(?:'(?<suf>\p{L}+))?(?![\p{L}\p{N}_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex Number();

    [GeneratedRegex(@"^[ıiuü]?nc[ıiuü](?<rest>\p{L}*)$", RegexOptions.CultureInvariant)]
    private static partial Regex OrdinalSuffix();

    [GeneratedRegex(@"°[ ]?(?:(?<s>[CF])(?![\p{L}]))?(?:'(?<suf>\p{L}+))?", RegexOptions.CultureInvariant)]
    private static partial Regex Degree();

    [GeneratedRegex(@"[&@%]", RegexOptions.CultureInvariant)]
    private static partial Regex Symbol();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    [GeneratedRegex(@"(?<![\p{L}\p{N}_])(?<w>[A-ZÇĞİÖŞÜÂÎÛ]{2,})(?:'(?<suf>\p{L}+))?(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant)]
    private static partial Regex Capitals();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    // An em dash, or an en dash with a space on a side, that does not stand between two numbers ("1914 – 1918" is a range).
    [GeneratedRegex(@"(?<!\d[ ]*)[ ]*(?:—|(?<=[ ])–|–(?=[ ]))[ ]*(?![ ]*\d)", RegexOptions.CultureInvariant)]
    private static partial Regex WordDash();
}
