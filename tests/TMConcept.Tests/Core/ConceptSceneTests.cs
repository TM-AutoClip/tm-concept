using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Tests.Desktop;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Core;

/// <summary>
/// The scenes of a concept come as JSON (20 Nov 2026): the prompt states the count the program computed from the target length, the program
/// writes the stored [on screen: …] text, the deterministic scene check finds too few scenes, empty on-screen lines and long narration, and a
/// failing concept gets one repair request with those findings. Fake model only.
/// </summary>
public sealed class ConceptSceneTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static Profile Profile(string language, int seconds = 60) => new()
    {
        Name = "Byzantium",
        ContentLanguage = language,
        Format = VideoFormat.Vertical,
        TargetSeconds = seconds,
        ConceptText = "A thousand years of Byzantium.",
    };

    private static string Scenes(int count, string label = "on screen") =>
        string.Join("\n", Enumerable.Range(1, count).Select(n => $"[{label}: map {n}]\nPart {n} of the story."));

    [Theory]
    [InlineData(15, 1, 1)]
    [InlineData(30, 2, 1)]
    [InlineData(60, 4, 3)]
    [InlineData(75, 6, 4)]
    [InlineData(90, 7, 5)]
    [InlineData(180, 13, 10)]
    public void TheSceneCountComesFromTheTargetLengthWithASmallTolerance(int seconds, int target, int minimum)
    {
        ConceptSceneCheck.Target(seconds).ShouldBe(target);
        ConceptSceneCheck.Minimum(seconds).ShouldBe(minimum);
    }

    [Fact]
    public void AConceptWithEnoughScenesPasses() =>
        ConceptSceneCheck.Check(Scenes(4), ContentLanguages.English, 60).ShouldBeEmpty();

    [Fact]
    public void AConceptWithoutSceneLinesFailsWithTooFewAndAnEmptyScreen()
    {
        var problems = ConceptSceneCheck.Check("The whole story told in one go, without any scene line.", ContentLanguages.English, 60);

        problems.ShouldBe([new SceneProblem(SceneProblemKind.TooFew, 0, 1, 3, 4), new SceneProblem(SceneProblemKind.EmptyVisual, 1, 0, 0)]);
        problems[0].ToString().ShouldBe("1 scenes, at least 3 needed");
    }

    [Fact]
    public void AnEmptyOnScreenLineAndALongNarrationAreFound()
    {
        var talk = string.Join(" ", Enumerable.Repeat("word", 80));
        var content = "[on screen: walls]\nShort.\n[on screen: ]\nAlso short.\n[on screen: fleet]\n" + talk;

        var problems = ConceptSceneCheck.Check(content, ContentLanguages.English, 30);

        problems.ShouldBe([new SceneProblem(SceneProblemKind.EmptyVisual, 2, 0, 0), new SceneProblem(SceneProblemKind.LongNarration, 3, 31, 25)],
            "80 words at 2.6 words a second are about 31 s");
    }

    [Fact]
    public void TheSceneListBecomesTheStoredTextWithTheLabelOfThePromptLanguage()
    {
        const string answer = """
            {"konseptler":[{"baslik":"Surlar","ozet":"Bir. İki.","sahneler":[
              {"ekranda":"Kara surları [harita]","seslendirme":"Surlar bin yıl dayandı."},
              {"ekranda":"","seslendirme":"Sonra top geldi."},
              {"ekranda":"","seslendirme":""}]}]}
            """;

        var concept = ConceptResponseParser.Parse(answer, ConceptResponseParser.SceneLabel(ContentLanguages.Turkish)).Concepts.ShouldHaveSingleItem();

        concept.Content.ShouldBe("[ekranda: Kara surları (harita)]\nSurlar bin yıl dayandı.\n[ekranda: ]\nSonra top geldi.",
            "brackets of the model become round ones; a scene with nothing is dropped");
        ConceptResponseParser.SceneLabel(ContentLanguages.German).ShouldBe("on screen");
        ConceptResponseParser.Parse("""{"konseptler":[{"baslik":"A","ozet":"s","icerik":"[x]\nHello"}]}""").Concepts.Single().Content
            .ShouldBe("[x]\nHello", "an AI edit still answers with its free text");
    }

    [Fact]
    public void ThePromptStatesTheSceneCountAndTheSchemaAsksForTheScenesWithoutACount()
    {
        var english = ConceptPromptBuilder.Build(new ConceptPromptInput(Profile(ContentLanguages.English), PromptMemory.Empty, string.Empty, 3));
        var turkish = ConceptPromptBuilder.Build(new ConceptPromptInput(Profile(ContentLanguages.Turkish), PromptMemory.Empty, string.Empty, 3));

        english.System.ShouldContain("write about 4 scenes (about one scene every 13.5 s; at least 3)");
        english.System.ShouldContain("about 13.5 s of speech, never more than 65 words or 25 s", Case.Sensitive,
            "25 s at 2.6 words a second is the most, the target per scene is 13.5 s (one figure only made a small model write long scenes)");
        turkish.System.ShouldContain("yaklaşık 4 sahne yaz (yaklaşık 13.5 sn'de bir sahne; en az 3)");
        turkish.System.ShouldContain("yaklaşık 13.5 sn konuşma, asla 57 kelimeden ya da 25 sn'den uzun değil");
        english.User.ShouldContain("\"sahneler\":[{\"ekranda\":\"…\",\"seslendirme\":\"…\"}]");
        english.System.ShouldNotContain("{");
        var schema = english.Schema.ToJsonString();
        schema.ShouldContain("\"sahneler\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"ekranda\":{\"type\":\"string\"},\"seslendirme\":{\"type\":\"string\"}}");
        schema.ShouldNotContain("icerik");
        schema.ShouldNotContain("minItems", Case.Sensitive, "Anthropic accepts no minItems above 1: the count stays in the prompt");
        ConceptPromptBuilder.DocumentSchema().ToJsonString().ShouldContain("\"required\":[\"baslik\",\"ozet\",\"sahneler\",\"kaynaklar\"]");
        ConceptPromptBuilder.Schema().ToJsonString().ShouldContain("icerik", Case.Sensitive, "the AI edit keeps its free text");
    }

    [Fact]
    public void TheRepairRequestNamesEveryFindingInThePromptLanguage()
    {
        var concept = new ParsedConcept("Walls «and» towers", "s", "Flat text.");
        IReadOnlyList<SceneProblem> problems = [new(SceneProblemKind.TooFew, 0, 1, 3, 4), new(SceneProblemKind.EmptyVisual, 1, 0, 0),
            new(SceneProblemKind.LongNarration, 2, 31, 25)];

        var english = ConceptPromptBuilder.BuildRepair(Profile(ContentLanguages.English), [new ConceptRepairItem(concept, problems)], grounding: null);
        var turkish = ConceptPromptBuilder.BuildRepair(Profile(ContentLanguages.Turkish), [new ConceptRepairItem(concept, problems)], Grounding());

        english.ShouldContain("1. «Walls \"and\" towers»\nPROBLEMS: 1 scenes, at least 3 needed (write about 4 scenes); scene 1 has an empty \"ekranda\"; "
            + "scene 2 narration is about 31 s, at most 25 s allowed (split the scene)\nCURRENT TEXT:\n«Flat text.»");
        english.ShouldContain("Write the 1 corrected concepts in the same order");
        english.ShouldNotContain("kaynaklar");
        english.ShouldNotContain("DOCUMENT");
        english.ShouldContain("each one complete and with its title exactly as given");
        turkish.ShouldContain("SORUNLAR: 1 sahne var, en az 3 olmalı (yaklaşık 4 sahne yaz); 1. sahnenin \"ekranda\" alanı boş");
        turkish.ShouldContain("\"kaynaklar\":[1]", Case.Sensitive, "a documents run keeps its document numbers");
        turkish.ShouldContain("YALNIZ aşağıdaki BELGE PARÇALARINDAKİ bilgileri kullan", Case.Sensitive,
            "the passages are in the batch's user part, which the repair does not repeat: it gets them again with the rule");
        turkish.ShouldContain("BELGELER (numara: başlık):\n1: «Chronicle»");
        turkish.ShouldContain("[1] «p. 3»: «The chain closed the Golden Horn in 1453.»");
    }

    private static TMConcept.Core.Documents.DocumentGrounding Grounding() =>
        new([new TMConcept.Core.Documents.GroundingDocument(1, Guid.NewGuid(), "Chronicle")], [new TMConcept.Core.Documents.GroundingPassage(1, "p. 3", "The chain closed the Golden Horn in 1453.")]);

    [Fact]
    public void RepairAnswersAreMatchedByTitleAndByPositionOnlyWhenTheCountsAgree()
    {
        static ParsedConcept C(string title) => new(title, "s", "text of " + title);
        IReadOnlyList<ParsedConcept> sent = [C("Walls"), C("Fleet"), C("Fire")];

        // One left out and the others swapped: matched by title, the left-out one gets nothing (never another concept's story).
        var pairs = ConceptRepairMatch.Match(sent, [C("FIRE!"), C("fleet")]);
        pairs[0].ShouldBeNull();
        pairs[1]!.Content.ShouldBe("text of fleet");
        pairs[2]!.Content.ShouldBe("text of FIRE!");

        // The same count with one title reworded: the reworded one takes its own position; an answer whose title is another concept's never does.
        pairs = ConceptRepairMatch.Match(sent, [C("The walls of the city"), C("Fleet"), C("Fire")]);
        pairs[0]!.Title.ShouldBe("The walls of the city");
        pairs = ConceptRepairMatch.Match(sent, [C("Fleet"), C("Something"), C("Fire")]);
        pairs[0].ShouldBeNull();
        pairs[1]!.Title.ShouldBe("Fleet");
        pairs[2]!.Title.ShouldBe("Fire");

        // Fewer answers than concepts and no title match: nothing is paired by position.
        ConceptRepairMatch.Match(sent, [C("Other")]).ShouldAllBe(p => p == null);
    }

    [AvaloniaFact]
    public async Task AConceptWithoutScenesGetsOneRepairWithItsFindings()
    {
        const string flat = """{"konseptler":[{"baslik":"Greek fire","ozet":"One. Two.","icerik":"The fleet burned in one long story without scenes."}]}""";
        var fake = new FakeChat().Answer(_ => Answer(flat))
            .Answer(_ => Answer("{\"konseptler\":[{\"baslik\":\"Greek fire\",\"ozet\":\"One. Two.\"," + FakeChat.SceneList("The fleet") + "}]}"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, new IdeaBrief(string.Empty, 1), model), null, null, None);

        fake.Requests.Count.ShouldBe(2);
        var repair = fake.Requests[1];
        repair.System.ShouldBe(fake.Requests[0].System, "the cached system part is read again");
        repair.User.ShouldContain("PROBLEMS: 1 scenes, at least 4 needed (write about 6 scenes); scene 1 has an empty \"ekranda\"");
        repair.User.ShouldContain("«The fleet burned in one long story without scenes.»");
        var concept = result.Accepted.ShouldHaveSingleItem();
        concept.Content.ShouldStartWith("[on screen: The fleet 1]\nPart 1: The fleet.\n[on screen: The fleet 2]");
        result.ScenesMissing.ShouldBe(0);
        result.Run.OutputTokens.ShouldBe(2 * Usage.Output, "the repair is paid and counted");
    }

    [AvaloniaFact]
    public async Task AConceptThatStillFailsAfterItsRepairIsKeptAndCounted()
    {
        const string flat = """{"konseptler":[{"baslik":"Greek fire","ozet":"One. Two.","sahneler":[{"ekranda":"","seslendirme":"All in one."}]}]}""";
        var fake = new FakeChat().Answer(_ => Answer(flat)).Answer(_ => Answer(flat));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, new IdeaBrief(string.Empty, 1), model), null, null, None);

        fake.Requests.Count.ShouldBe(2, "one repair, never a second");
        result.Accepted.ShouldHaveSingleItem().Content.ShouldBe("[on screen: ]\nAll in one.");
        result.ScenesMissing.ShouldBe(1);
        result.ErrorCode.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ARepairThatLeavesAConceptOutOrReordersThemNeverPutsOneConceptInPlaceOfAnother()
    {
        static string Flat(string title) => "{\"baslik\":\"" + title + "\",\"ozet\":\"" + Guid.NewGuid().ToString("N") + "\",\"icerik\":\"" + title + " told in one go.\"}";
        static string Fixed(string title) => "{\"baslik\":\"" + title + "\",\"ozet\":\"" + Guid.NewGuid().ToString("N") + "\"," + FakeChat.SceneList(title) + "}";
        var fake = new FakeChat().Answer(_ => Answer("{\"konseptler\":[" + Flat("Walls") + "," + Flat("Fleet") + "," + Flat("Fire") + "]}"))
            .Answer(_ => Answer("{\"konseptler\":[" + Fixed("Fire") + "," + Fixed("Fleet") + "]}"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, new IdeaBrief(string.Empty, 3), model), null, null, None);

        fake.Requests[1].Stream.ShouldBeTrue("the repair is streamed like the batch: no single time limit cuts a slow model");
        var byTitle = result.Accepted.ToDictionary(c => c.Title);
        byTitle.Keys.ShouldBe(["Walls", "Fleet", "Fire"], ignoreOrder: true);
        byTitle["Walls"].Content.ShouldBe("Walls told in one go.", "the model left it out: it keeps its own text");
        byTitle["Fleet"].Content.ShouldStartWith("[on screen: Fleet 1]\nPart 1: Fleet.");
        byTitle["Fire"].Content.ShouldStartWith("[on screen: Fire 1]\nPart 1: Fire.");
        result.ScenesMissing.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task ACutOffRepairStillGivesItsCompleteConceptsAndItsCostIsCounted()
    {
        static string Flat(string title) => "{\"baslik\":\"" + title + "\",\"ozet\":\"" + Guid.NewGuid().ToString("N") + "\",\"icerik\":\"" + title + " told in one go.\"}";
        var cut = "{\"konseptler\":[{\"baslik\":\"Walls\",\"ozet\":\"One. Two.\"," + FakeChat.SceneList("Walls") + "},{\"baslik\":\"Fleet\",\"ozet\":\"On";
        var fake = new FakeChat().Answer(_ => Answer("{\"konseptler\":[" + Flat("Walls") + "," + Flat("Fleet") + "]}"))
            .Answer(_ => throw new TMConcept.Core.Providers.ProviderException("cut") { Code = "truncated", PartialText = cut, ChargedUsage = Usage });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, new IdeaBrief(string.Empty, 2), model), null, null, None);

        var byTitle = result.Accepted.ToDictionary(c => c.Title);
        byTitle["Walls"].Content.ShouldStartWith("[on screen: Walls 1]", Case.Sensitive, "the complete concept before the cut was paid for and is used");
        byTitle["Fleet"].Content.ShouldBe("Fleet told in one go.");
        result.ScenesMissing.ShouldBe(1);
        result.ErrorCode.ShouldBeEmpty("a cut repair does not end the run");
        result.Run.OutputTokens.ShouldBe(2 * Usage.Output, "what the cut repair charged is counted");
    }

    private static readonly ChatUsage Usage = new(600, 0, 0, 120);

    private static ChatResponse Answer(string text) => new(text, Usage, null, "end_turn", "r");

    private static async Task<(Profile Profile, ResolvedModel Model)> PrepareAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = "fake-test-key-0123456789abcdef" }, None);
        var catalog = h.Get<IModelCatalog>().Find("anthropic", "claude-haiku-5-5")!;
        var key = (await h.Get<IApiKeyStore>().ListAsync("anthropic", None)).First();
        return (profile, new ResolvedModel(catalog, key, string.Empty));
    }
}
