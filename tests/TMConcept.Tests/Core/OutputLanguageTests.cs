using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Subtitles;
using TMConcept.Core.Videos;

namespace TMConcept.Tests.Core;

/// <summary>Output language apart from the language of the user's notes: prompts, rates, word checks and subtitles of other languages.</summary>
public sealed class OutputLanguageTests
{
    private static Profile Profile(string language, string concept) => new()
    {
        Name = "Byzanz",
        ContentLanguage = language,
        Format = VideoFormat.Vertical,
        TargetSeconds = 60,
        ConceptText = concept,
    };

    [Fact]
    public void AGermanVideoGetsTheEnglishPromptThatNamesGermanAndAllowsTurkishNotes()
    {
        var prompt = ConceptPromptBuilder.Build(new ConceptPromptInput(Profile(ContentLanguages.German, "Bizans tarihi, sakin bir anlatım."), [],
            ["Der Nika-Aufstand"], "Vikingler hakkında olsun", 5));

        prompt.System.ShouldStartWith("You write content for a video channel.");
        prompt.System.ShouldContain("every [on screen: …] line in German");
        prompt.System.ShouldContain("may be written in another language, for example Turkish");
        prompt.System.ShouldContain("answer only in German");
        prompt.System.ShouldContain("(\"… — Teil 3\")");
        prompt.System.ShouldContain("≈ 132 words of narration in German", Case.Sensitive, "German is read at 2.2 words a second");
        prompt.System.ShouldContain("CHANNEL CONCEPT: «Bizans tarihi, sakin bir anlatım.»", Case.Sensitive, "the user's Turkish notes go in unchanged");
        prompt.System.ShouldNotContain("{");
        prompt.User.ShouldContain("EXTRA REQUEST: «Vikingler hakkında olsun»");
    }

    [Fact]
    public void ATurkishVideoKeepsTheTurkishPromptAndSaysTheNotesMayBeInAnotherLanguage()
    {
        var prompt = ConceptPromptBuilder.Build(new ConceptPromptInput(Profile(ContentLanguages.Turkish, "Calm Byzantine history."), [], [], string.Empty, 5));

        prompt.System.ShouldStartWith("Sen bir video kanalının içerik yazarısın.");
        prompt.System.ShouldContain("satırının metnini Türkçe yaz");
        prompt.System.ShouldContain("yanıtı yalnız Türkçe yaz");
        prompt.System.ShouldContain("≈ 138 kelime Türkçe seslendirme");
    }

    [Fact]
    public void EveryOutputLanguageHasItsRatesSampleAndADistinctCode()
    {
        ContentLanguages.All.Count.ShouldBe(12);
        ContentLanguages.All.Distinct().Count().ShouldBe(ContentLanguages.All.Count);
        ContentLanguages.All.ShouldContain(ContentLanguages.German);
        foreach (var language in ContentLanguages.Definitions)
        {
            language.WordsPerSecond.ShouldBeInRange(1.5, 3.5, language.Code);
            language.CharactersPerToken.ShouldBeInRange(2, 4.5, language.Code);
            language.Sample.Length.ShouldBeGreaterThan(10, language.Code);
            language.Culture.TwoLetterISOLanguageName.ShouldBe(language.Code);
            language.PromptLanguage.ShouldBe(language.Code == ContentLanguages.Turkish ? ContentLanguages.Turkish : ContentLanguages.English);
        }
    }

    [Fact]
    public void RatesFollowTheLanguageAndAnUnknownCodeGetsEnglishRates()
    {
        ConceptPromptBuilder.Words(ContentLanguages.Turkish, 60).ShouldBe(138, "kept from before");
        ConceptPromptBuilder.Words(ContentLanguages.English, 60).ShouldBe(156, "kept from before");
        ConceptPromptBuilder.Words(ContentLanguages.Spanish, 60).ShouldBe(156);
        ConceptPromptBuilder.Words(ContentLanguages.Russian, 60).ShouldBe(126);
        ConceptPromptBuilder.Words("xx", 60).ShouldBe(156);
        OutputLanguages.Of("xx").PromptLanguage.ShouldBe(ContentLanguages.English);
        OutputLanguages.Of(null).Code.ShouldBe(ContentLanguages.English);
        OutputLanguages.IsKnown("xx").ShouldBeFalse();

        var costs = new CostCalculator();
        var text = new string('a', 260);
        costs.Tokens(text, ContentLanguages.Turkish).ShouldBe(100);
        costs.Tokens(text, ContentLanguages.Polish).ShouldBeGreaterThan(costs.Tokens(text, ContentLanguages.English));
        ConceptOutputBudget.TokensPerConcept(ContentLanguages.German, 60).ShouldBeGreaterThan(ConceptOutputBudget.TokensPerConcept(ContentLanguages.English, 60));
        VideoCostEstimator.Milliseconds("eins zwei drei vier fünf sechs sieben acht neun zehn elf", ContentLanguages.German).ShouldBe(5000);
    }

    [Fact]
    public void NamesAreShownInTheInterfaceLanguage()
    {
        OutputLanguages.NameIn(ContentLanguages.German, ContentLanguages.Turkish).ShouldBe("Almanca");
        OutputLanguages.NameIn(ContentLanguages.German, ContentLanguages.English).ShouldBe("German");
        OutputLanguages.Of(ContentLanguages.German).Native.ShouldBe("Deutsch");
    }

    [Fact]
    public void TheWordCheckKnowsPartWordsAndFillerWordsOfOtherLanguages()
    {
        static DuplicateSubject S(string title) => new(Guid.NewGuid(), 1, title, string.Empty);

        DuplicateCheck.Sift([S("Der Nika-Aufstand — Teil 3")], [S("Der Nika-Aufstand — Teil 2")], ContentLanguages.German).Single().Result.Kind
            .ShouldBe(DuplicateKind.Continuation);
        DuplicateCheck.Sift([S("La caída de Constantinopla — Parte 2")], [S("La caída de Constantinopla, parte 2")], ContentLanguages.Spanish).Single()
            .Result.Kind.ShouldBe(DuplicateKind.Duplicate);
        DuplicateCheck.SplitPart("Падение Константинополя — Часть 4").ShouldBe(("Падение Константинополя", 4));
        DuplicateCheck.SplitPart("Napoléon Bonaparte 3").Part.ShouldBe(0, "\"parte\" inside a name is no part word");

        // German filler words do not count: only "Konstantinopel" and "Fall" are left on both sides.
        var german = DuplicateCheck.Features(S("Der Fall von Konstantinopel"), ContentLanguages.German);
        german.Words.ShouldBe(["fall", "konsta"], ignoreOrder: true);
    }

    [Fact]
    public void SubtitlesEndSentencesBeforeQuotesAndKeepEveryLetterOfALongWord()
    {
        var cues = SubtitleBuilder.Build([new SubtitleSource(0, 6000, "Er sagte: „Wir halten.“ Dann kam die Donaudampfschifffahrtsgesellschaftskapitänswitwe.")]);

        cues[0].Lines.ShouldBe(["Er sagte: „Wir halten.“"]);
        string.Concat(cues.Skip(1).SelectMany(c => c.Lines)).Replace(" ", string.Empty, StringComparison.Ordinal)
            .ShouldBe("DannkamdieDonaudampfschifffahrtsgesellschaftskapitänswitwe.");
        cues.ShouldAllBe(c => c.Lines.All(l => l.Length <= SubtitleBuilder.LineMax));
        SubtitleBuilder.EndsSentence("halten.»").ShouldBeTrue();
        SubtitleBuilder.EndsSentence("¿Qué?").ShouldBeTrue();
        SubtitleBuilder.EndsSentence("1453)").ShouldBeFalse();
    }

    [Fact]
    public void TheVideoCodeModelIsToldTheVideoLanguage()
    {
        var input = RenderInputBuilder.Build("Der Fall", ContentLanguages.German, [new RenderSceneSource("Karte", "Text", string.Empty, 2000)], 1080, 1920, 30);

        VideoCodePrompt.User(input).ShouldContain("Video language: German (de). Any word you write into the code yourself is in German.");
    }
}
