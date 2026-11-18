using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Profiles;
using TMConcept.Core.Pronunciation;

namespace TMConcept.Tests.Core;

/// <summary>
/// The Turkish narration fixes of 0.1.24: circumflex vowels reach the voice plain ("hikâyesi" was misread: Supertonic splits â into a and a
/// combining circumflex it hardly ever heard in Turkish), dashes and typographic marks become what every voice knows, and the concept
/// prompts ask for spellings without a circumflex.
/// </summary>
public sealed class TurkishVoiceTextTests
{
    private static readonly TurkishSpeechTextNormalizer Normalizer = new();

    [Theory]
    [InlineData("Şifrelerin gerçek hikâyesi", "Şifrelerin gerçek hikayesi")]
    [InlineData("Bu hâlâ bir sır", "Bu hala bir sır")]
    [InlineData("hâtâsı", "hatası")]
    [InlineData("Millî Mücadele", "Milli Mücadele")]
    [InlineData("Rûm ve Kâbe", "Rum ve Kabe")]
    [InlineData("ÂLİM", "Alim")]
    [InlineData("Îmâr", "İmar")]
    public void CircumflexVowelsArePlainForTheVoice(string written, string spoken) => Normalizer.Normalize(written).ShouldBe(spoken);

    [Fact]
    public void DecomposedCircumflexIsComposedFirst()
    {
        // A model may send "a" + U+0302 (combining circumflex): it must not survive either.
        var decomposed = "hikâyesi";
        Normalizer.Normalize(decomposed).ShouldBe("hikayesi");
    }

    [Fact]
    public void SuffixesAfterACircumflexWordStillAgree()
    {
        Normalizer.Normalize("Kâbe'ye 3 kez gitti").ShouldBe("Kabe'ye üç kez gitti");
    }

    [Theory]
    [InlineData("Savaş bitti — ama barış gelmedi.", "Savaş bitti, ama barış gelmedi.")]
    [InlineData("Savaş bitti—ama barış gelmedi.", "Savaş bitti, ama barış gelmedi.")]
    [InlineData("Kent – yani İstanbul – düştü.", "Kent, yani İstanbul, düştü.")]
    [InlineData("Ve sonra…", "Ve sonra...")]
    [InlineData("“Kırılmaz” denirdi.", "\"Kırılmaz\" denirdi.")]
    public void DashesAndTypographicMarksBecomeWhatEveryVoiceKnows(string written, string spoken) => Normalizer.Normalize(written).ShouldBe(spoken);

    [Fact]
    public void ARangeWithAnEnDashStaysARange()
    {
        Normalizer.Normalize("1914 – 1918").ShouldBe("bin dokuz yüz on dört ile bin dokuz yüz on sekiz");
        Normalizer.Normalize("1914–1918").ShouldBe("bin dokuz yüz on dört ile bin dokuz yüz on sekiz");
    }

    [Fact]
    public void PlainVowelsLeavesOtherTurkishLettersAlone()
    {
        TurkishSpeechTextNormalizer.PlainVowels("çğıöşü ÇĞİÖŞÜ").ShouldBe("çğıöşü ÇĞİÖŞÜ");
    }

    [Theory]
    [InlineData(ContentLanguages.Turkish, "Şapkalı harf (â, î, û) kullanma", "yaklaşıktır")]
    [InlineData(ContentLanguages.English, "never use circumflex letters", "approximate")]
    public void TheConceptPromptsAskForAnApproximateLengthAndNoCircumflex(string language, string circumflex, string approximate)
    {
        var profile = new Profile { Name = "p", ContentLanguage = language, Format = VideoFormat.Vertical, TargetSeconds = 75, ConceptText = "c" };
        var prompt = ConceptPromptBuilder.Build(new ConceptPromptInput(profile, [], PromptMemory.Empty, string.Empty, 3));

        prompt.System.ShouldContain(circumflex);
        prompt.System.ShouldContain(approximate);
    }
}
