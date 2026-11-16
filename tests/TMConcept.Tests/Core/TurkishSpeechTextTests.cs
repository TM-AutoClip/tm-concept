using System.Globalization;
using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Pronunciation;
using TMConcept.Core.Text;

namespace TMConcept.Tests.Core;

/// <summary>
/// The Turkish spoken form: numbers, ordinals, dates, times, percentages, currencies, units, Roman numerals, abbreviations, capital
/// abbreviations, symbols and suffixes agreed with the reading; the dictionary's exact-case words and suffix agreement.
/// </summary>
public sealed class TurkishSpeechTextTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");
    private static readonly TurkishSpeechTextNormalizer Normalizer = new();

    private static string N(string text) => Normalizer.Normalize(text);

    [Theory]
    [InlineData("0", "sıfır")]
    [InlineData("7", "yedi")]
    [InlineData("100", "yüz")]
    [InlineData("1000", "bin")]
    [InlineData("1453", "bin dört yüz elli üç")]
    [InlineData("21000", "yirmi bir bin")]
    [InlineData("1000000", "bir milyon")]
    [InlineData("1.250", "bin iki yüz elli")]
    [InlineData("2.500.000", "iki milyon beş yüz bin")]
    [InlineData("3,5", "üç virgül beş")]
    [InlineData("3,05", "üç virgül sıfır beş")]
    [InlineData("2.5", "iki nokta beş")]
    [InlineData("007", "sıfır sıfır yedi")]
    [InlineData("-5 derece", "eksi beş derece")]
    public void NumbersAreReadInWords(string written, string spoken) => N(written).ShouldBe(spoken);

    [Theory]
    [InlineData("1453'te", "bin dört yüz elli üçte")]
    [InlineData("4'ü", "dördü")]
    [InlineData("6'da", "altıda")]
    [InlineData("1990'larda", "bin dokuz yüz doksanlarda")]
    [InlineData("2'nci", "ikinci")]
    [InlineData("5'inci", "beşinci")]
    [InlineData("3. sırada", "üçüncü sırada")]
    [InlineData("21. yüzyıl", "yirmi birinci yüzyıl")]
    [InlineData("1. Dünya Savaşı", "Birinci Dünya Savaşı")]
    [InlineData("3.'sü geldi", "üçüncüsü geldi")]
    [InlineData("Yıl 1453. Sonra geldi.", "Yıl bin dört yüz elli üç. Sonra geldi.")]
    public void SuffixesAndOrdinalsFollowTheReading(string written, string spoken) => N(written).ShouldBe(spoken);

    [Theory]
    [InlineData("14.10.2026", "on dört Ekim iki bin yirmi altı")]
    [InlineData("29/05/1453'te", "yirmi dokuz Mayıs bin dört yüz elli üçte")]
    [InlineData("2026-10-14", "on dört Ekim iki bin yirmi altı")]
    [InlineData("29 Mayıs 1453", "yirmi dokuz Mayıs bin dört yüz elli üç")]
    [InlineData("09:30'da", "dokuz otuzda")]
    [InlineData("14:00", "on dört")]
    [InlineData("saat 14.30", "saat on dört otuz")]
    [InlineData("1914-1918", "bin dokuz yüz on dört ile bin dokuz yüz on sekiz")]
    [InlineData("MÖ 330", "milattan önce üç yüz otuz")]
    [InlineData("MS 1071'de", "milattan sonra bin yetmiş birde")]
    [InlineData("M.Ö. 3000", "milattan önce üç bin")]
    public void DatesTimesYearsAndErasAreRead(string written, string spoken) => N(written).ShouldBe(spoken);

    [Theory]
    [InlineData("%40", "yüzde kırk")]
    [InlineData("%40'ı", "yüzde kırkı")]
    [InlineData("1.250 TL", "bin iki yüz elli lira")]
    [InlineData("12,50 TL", "on iki lira elli kuruş")]
    [InlineData("100 TL'ye", "yüz liraya")]
    [InlineData("$5", "beş dolar")]
    [InlineData("€20", "yirmi avro")]
    [InlineData("$5 milyon", "beş milyon dolar")]
    [InlineData("3,5 kg", "üç virgül beş kilogram")]
    [InlineData("25km", "yirmi beş kilometre")]
    [InlineData("5 m", "beş metre")]
    [InlineData("120 km/sa", "saatte yüz yirmi kilometre")]
    [InlineData("25°C", "yirmi beş derece")]
    [InlineData("5 GB", "beş gigabayt")]
    [InlineData("Ha, anladım.", "Ha, anladım.")]
    public void PercentagesCurrenciesAndUnitsAreRead(string written, string spoken) => N(written).ShouldBe(spoken);

    [Theory]
    [InlineData("II. Mehmed", "İkinci Mehmed")]
    [InlineData("IV. Murat'ın", "Dördüncü Murat'ın")]
    [InlineData("XV. yüzyıl", "on beşinci yüzyıl")]
    [InlineData("XV yüzyılda", "on beşinci yüzyılda")]
    [InlineData("M. Kemal", "M. Kemal")]
    [InlineData("Prof. Dr. Ayşe", "profesör doktor Ayşe")]
    [InlineData("elma, armut vb.", "elma, armut ve benzeri.")]
    [InlineData("vb. şeyler", "ve benzeri şeyler")]
    [InlineData("TBMM'de", "te be me mede")]
    [InlineData("ABD", "a be de")]
    [InlineData("THY", "te ha ye")]
    [InlineData("DİKKAT", "Dikkat")]
    [InlineData("BU VE ŞU", "bu ve şu")]
    [InlineData("TÜBİTAK'a", "Tübitaka")]
    [InlineData("Louis XIV", "Louis on dördüncü")]
    [InlineData("3 + 4 = 7", "üç artı dört eşittir yedi")]
    [InlineData("Ali & Veli", "Ali ve Veli")]
    [InlineData("1920x1080", "bin dokuz yüz yirmi çarpı bin seksen")]
    [InlineData("MP3", "me pe üç")]
    public void RomanNumeralsAbbreviationsAndSymbolsAreRead(string written, string spoken) => N(written).ShouldBe(spoken);

    [Fact]
    public void AHistoryNarrationIsReadAsASpeakerWouldSayIt()
    {
        N("II. Mehmed 29 Mayıs 1453'te İstanbul'u aldı; XV. yüzyılın en büyük olayıydı. Ordunun %80'i, yani 80.000 asker, 3. günde surlara ulaştı.")
            .ShouldBe("İkinci Mehmed yirmi dokuz Mayıs bin dört yüz elli üçte İstanbul'u aldı; on beşinci yüzyılın en büyük olayıydı. "
                + "Ordunun yüzde sekseni, yani seksen bin asker, üçüncü günde surlara ulaştı.");
    }

    [Fact]
    public void OnlyTurkishIsHandledAndEmptyTextStaysEmpty()
    {
        Normalizer.Supports(ContentLanguages.Turkish).ShouldBeTrue();
        Normalizer.Supports(ContentLanguages.English).ShouldBeFalse();
        N(string.Empty).ShouldBe(string.Empty);
        N("Bugün hava çok güzel.").ShouldBe("Bugün hava çok güzel.");
    }

    [Theory]
    [InlineData("Gugıl", "ın", "Gugılın")]
    [InlineData("Gugıl", "nın", "Gugılın")]
    [InlineData("Nato", "ya", "Natoya")]
    [InlineData("Nato", "a", "Natoya")]
    [InlineData("Bordo", "nun", "Bordonun")]
    [InlineData("Ayfon", "umdan", "Ayfonumdan")]
    [InlineData("Epıl", "e", "Epıla")]
    [InlineData("Maykrosoft", "ta", "Maykrosoftta")]
    [InlineData("Maykrosoft", "da", "Maykrosoftta")]
    [InlineData("dört", "ü", "dördü")]
    [InlineData("üç", "de", "üçte")]
    [InlineData("te be me me", "de", "te be me mede")]
    public void SuffixesAgreeWithTheSpokenStem(string stem, string suffix, string joined) => TurkishHarmony.Attach(stem, suffix).ShouldBe(joined);

    [Fact]
    public void NumberWordsAndOrdinalsFollowTurkishRules()
    {
        TurkishNumbers.Cardinal(101).ShouldBe("yüz bir");
        TurkishNumbers.Cardinal(1_001_000).ShouldBe("bir milyon bin");
        TurkishNumbers.Cardinal(-42).ShouldBe("eksi kırk iki");
        TurkishNumbers.Ordinal(4).ShouldBe("dördüncü");
        TurkishNumbers.Ordinal(6).ShouldBe("altıncı");
        TurkishNumbers.Ordinal(9).ShouldBe("dokuzuncu");
        TurkishNumbers.Ordinal(20).ShouldBe("yirminci");
        TurkishNumbers.Ordinal(1000).ShouldBe("bininci");
        TurkishNumbers.Cardinal(new string('9', 19)).Split(' ').Length.ShouldBe(19, "a very long number is read digit by digit");
        RomanNumerals.TryParse("MCMXIV", out var year).ShouldBeTrue();
        year.ShouldBe(1914);
        RomanNumerals.TryParse("IIII", out _).ShouldBeFalse();
        TurkishSpeechTextNormalizer.IsPronounceable("NATO").ShouldBeTrue();
        TurkishSpeechTextNormalizer.IsPronounceable("HTML").ShouldBeFalse();
        TurkishSpeechTextNormalizer.Spell("ĞW").ShouldBe("yumuşak ge çift ve");
    }

    [Fact]
    public void TheDictionaryJoinsSuffixesAndKeepsExactCaseWords()
    {
        IReadOnlyList<PronunciationRule> rules =
        [
            new("Google", "Gugıl", null), new("iPhone", "Ayfon", null), new("NATO", "Nato", null, ExactCase: true),
            new("IT", "Ay Ti", null, ExactCase: true), new("ChatGPT", "Çet Ci Pi Ti", null),
        ];

        PronunciationRewriter.Rewrite("Google'ın iPhone'umdan NATO'ya", rules, Turkish, Normalizer.Join).ShouldBe("Gugılın Ayfonumdan Natoya");
        PronunciationRewriter.Rewrite("IT ekibi it gibi", rules, Turkish, Normalizer.Join).ShouldBe("Ay Ti ekibi it gibi", "an exact word only as written");
        PronunciationRewriter.Rewrite("CHATGPT", rules, Turkish, Normalizer.Join).ShouldBe("Çet Ci Pi Ti", "capitals get the spoken form as written");
        PronunciationRewriter.Rewrite("CHATGPT", rules, Turkish).ShouldBe("ÇET Cİ Pİ Tİ", "without a join the old case rule stays");
        PronunciationRewriter.Rewrite("Google'ın", rules, Turkish).ShouldBe("Gugıl'ın", "without a join the suffix stays as written");
    }

    [Fact]
    public void TheListFormatHasCategoriesAndExactWords()
    {
        var rules = BuiltInPronunciations.Parse("# c\n## Tech\nGoogle=Gugıl\n!IT=Ay Ti\n## Places\nNew York = Nüyork\n!=x\n");

        rules.Select(r => (r.Word, r.Spoken, r.ExactCase, r.Category)).ShouldBe(
        [
            ("Google", "Gugıl", false, "Tech"), ("IT", "Ay Ti", true, "Tech"), ("New York", "Nüyork", false, "Places"),
        ]);
    }

    [Fact]
    public void TheBuiltInTurkishListIsLargeCleanAndReadsTheDevelopersExamples()
    {
        var rules = BuiltInPronunciations.Of(ContentLanguages.Turkish);

        rules.Count.ShouldBeGreaterThan(400);
        rules.Select(r => PronunciationService.Key(r, Turkish)).ShouldBeUnique();
        rules.ShouldAllBe(r => r.Category.Length > 0 && r.Spoken.Length <= PronunciationService.WordMax && !r.Spoken.Any(char.IsDigit));
        rules.Select(r => r.Category).Distinct().Count().ShouldBeGreaterThanOrEqualTo(6);
        var spoken = rules.Where(r => !r.ExactCase).ToDictionary(r => r.Word, r => r.Spoken, StringComparer.Ordinal);
        spoken["Google"].ShouldBe("Gugıl");
        spoken["YouTube"].ShouldBe("Yutub");
        spoken["Microsoft"].ShouldBe("Maykrosoft");
        spoken["iPhone"].ShouldBe("Ayfon");
        spoken["Nvidia"].ShouldBe("Envidya");
        spoken["Machu Picchu"].ShouldBe("Maçu Piççu");
        spoken["Napoleon"].ShouldBe("Napolyon");
        spoken["Churchill"].ShouldBe("Çörçil");
        spoken["Bismarck"].ShouldBe("Bismark");
        spoken["Tenochtitlan"].ShouldBe("Tenoçtitlan");
    }
}
