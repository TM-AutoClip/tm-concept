using System.Globalization;
using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Pronunciation;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Desktop;

namespace TMConcept.Tests.Core;

/// <summary>The pronunciation dictionary: whole words, the language's own upper and lower case, built-in and user words.</summary>
public sealed class PronunciationTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");

    private static PronunciationRule R(string word, string spoken) => new(word, spoken, null);

    [Fact]
    public void OnlyWholeWordsAreReplacedAndTheirCaseIsKept()
    {
        IReadOnlyList<PronunciationRule> rules = [R("Viking", "Vikink")];

        PronunciationRewriter.Rewrite("Viking gemisi, viking çağı, VİKİNG!", rules, Turkish).ShouldBe("Vikink gemisi, vikink çağı, VİKİNK!");
        PronunciationRewriter.Rewrite("Viking'ler (Viking) geldi.", rules, Turkish).ShouldBe("Vikink'ler (Vikink) geldi.");
        PronunciationRewriter.Rewrite("Vikingler ve Vikingdom", rules, Turkish).ShouldBe("Vikingler ve Vikingdom", "a whole word only");
        PronunciationRewriter.Rewrite("VIKING", rules, Turkish).ShouldBe("VIKING", "Turkish lower case of VIKING is vıkıng, not viking");
        PronunciationRewriter.Rewrite("VIKING", rules, CultureInfo.GetCultureInfo("en")).ShouldBe("VIKINK", "English case rules know no dotless i");
    }

    [Fact]
    public void TheLongestMatchWinsAndAWordInsideAnotherIsLeftAlone()
    {
        IReadOnlyList<PronunciationRule> rules = [R("New", "Nyu"), R("New York", "Nüyork"), R("York", "Yoork")];

        PronunciationRewriter.Rewrite("New York ve New Jersey; Newyorklu York.", rules, Turkish).ShouldBe("Nüyork ve Nyu Jersey; Newyorklu Yoork.");
        PronunciationRewriter.Rewrite("ıspanak İzmir", [R("İzmir", "İzmiir"), R("ispanak", "x")], Turkish).ShouldBe("ıspanak İzmiir",
            "ı and i are different letters in Turkish");
    }

    [Fact]
    public void TheTurkishListHasVikingAndOtherLanguagesHaveNone()
    {
        var turkish = BuiltInPronunciations.Of(ContentLanguages.Turkish);
        turkish.ShouldContain(r => r.Word == "Viking" && r.Spoken == "Vikink");
        turkish.Count.ShouldBeInRange(20, 80, "a small starting set");
        turkish.Select(r => PronunciationRewriter.Fold(r.Word, Turkish)).Distinct().Count().ShouldBe(turkish.Count, "no word twice");
        turkish.ShouldAllBe(r => r.BuiltIn);
        BuiltInPronunciations.Of(ContentLanguages.German).ShouldBeEmpty();
        BuiltInPronunciations.Parse("# comment\nA=B\n=x\nC=\nD = E ").Select(r => (r.Word, r.Spoken)).ShouldBe([("A", "B"), ("D", "E")]);
    }

    [AvaloniaFact]
    public async Task TheUsersWordReplacesABuiltInOneUntilItIsRemoved()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var service = h.Get<PronunciationService>();

        (await service.PrepareAsync("Bir Viking gemisi.", ContentLanguages.Turkish, CancellationToken.None)).ShouldBe("Bir Vikink gemisi.");
        (await service.AddAsync(ContentLanguages.Turkish, "viking", "Vayking", CancellationToken.None)).ShouldBeNull();
        (await service.PrepareAsync("Bir Viking gemisi.", ContentLanguages.Turkish, CancellationToken.None)).ShouldBe("Bir Vayking gemisi.");
        (await service.AddAsync(ContentLanguages.Turkish, "VİKİNG", "Vikiing", CancellationToken.None)).ShouldBeNull("the same word in Turkish case");
        var rules = await service.RulesAsync(ContentLanguages.Turkish, CancellationToken.None);
        rules.Count(r => !r.BuiltIn).ShouldBe(1, "the same spelling changes the user's word instead of adding one");
        rules.ShouldNotContain(r => r.BuiltIn && r.Word == "Viking");
        (await service.PrepareAsync("Viking", ContentLanguages.English, CancellationToken.None)).ShouldBe("Viking", "a word belongs to one language");
        (await service.AddAsync(ContentLanguages.Turkish, " ", "x", CancellationToken.None)).ShouldBe("empty");
        (await service.AddAsync(ContentLanguages.Turkish, new string('a', 61), "x", CancellationToken.None)).ShouldBe("long");

        await service.DeleteAsync(rules.First(r => !r.BuiltIn).Entry!, CancellationToken.None);

        (await service.PrepareAsync("Bir Viking gemisi.", ContentLanguages.Turkish, CancellationToken.None)).ShouldBe("Bir Vikink gemisi.");
    }

    [AvaloniaFact]
    public async Task TheSettingsTabListsBuiltInWordsAndAddsAndRemovesTheUsersOwn()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var section = h.Get<VoiceSettingsViewModel>();
        await section.ActivateAsync();
        section.Language = section.Languages.First(l => l.Value == ContentLanguages.Turkish);
        await section.ActivateAsync();
        section.Words.ShouldContain(w => w.Word == "Viking" && !w.Own);

        section.NewWord = "Harald";
        section.NewSpoken = "Harald";
        await section.AddWordAsync();
        section.Words[0].Word.ShouldBe("Harald");
        section.Words[0].Own.ShouldBeTrue();
        section.NewWord.ShouldBeEmpty();

        await section.DeleteWordAsync(section.Words[0]);
        section.Words.ShouldNotContain(w => w.Word == "Harald");

        section.Language = section.Languages.First(l => l.Value == ContentLanguages.German);
        await section.ActivateAsync();
        section.Words.ShouldBeEmpty();
        section.Languages.First(l => l.Value == ContentLanguages.German).Label.ShouldContain("Deutsch");
    }
}
