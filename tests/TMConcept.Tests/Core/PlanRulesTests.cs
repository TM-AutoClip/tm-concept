using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Plans;
using TMConcept.Core.Render;
using TMConcept.Core.Videos;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.41 (F2): the plan rules the code enforces — each with a plan that breaks it and one that keeps it — their order after the structure,
/// the choice of the repaired answer, and the planner putting the findings into its single repair request (never a second one).
/// </summary>
public sealed class PlanRulesTests
{
    private static PlanCheck Check(int scenes, bool subtitles = true, double seconds = 10, int sentences = 2, bool pictures = true) => new(
        Enumerable.Range(1, scenes).Select(n => new PlanScene(n, seconds, "visual " + n, Enumerable.Range(1, sentences).Select(s => "Sentence " + s + ".").ToList(),
            pictures ? "picture " + n : string.Empty)).ToList(),
        Enumerable.Repeat(pictures, scenes).ToList(), subtitles);

    private static PlanItem Item(int scene, RenderPlan plan) => new() { Scene = scene, Plan = plan };

    private static RenderPlan Title(string text = "A title") => new() { Base = PlanBases.Title, Title = text };

    private static RenderPlan Map(double west = 20, params PlanBeat[] beats) => new()
    {
        Base = PlanBases.Map,
        Map = new PlanMap { Region = [west, 30, west + 25, 45] },
        Anchors = [new PlanAnchor { Id = "a", Label = "City", Lon = west + 5, Lat = 40 }, new PlanAnchor { Id = "b", Label = "Port", Lon = west + 10, Lat = 38 }],
        Beats = [.. beats],
    };

    private static PlanBeat Beat(int sentence, string focus = "", double zoom = 1, params PlanAction[] actions) => new()
    {
        Sentence = sentence,
        Camera = focus.Length > 0 || zoom > 1 ? new PlanCamera { Focus = focus, Zoom = zoom } : null,
        Actions = [.. actions],
    };

    private static PlanAction Pin(string at, string label = "") => new() { Type = PlanActions.Pin, At = at, Label = label };

    private static IReadOnlyList<string> Run(IPlanRule rule, PlanCheck check, params PlanItem[] items) => rule.Problems(items, check).ToList();

    [Fact]
    public void OneBackgroundFillsAtMostFortyPercentOfTheVideo()
    {
        var rule = new BackgroundShareRule();
        var check = Check(4);
        Run(rule, check, Item(1, Title()), Item(2, Title("Two")), Item(3, Map()), Item(4, new RenderPlan { Base = PlanBases.Image }))
            .ShouldHaveSingleItem().ShouldContain("one background (title cards) fills 50% of the video (scenes 1, 2; at most 40%)");
        Run(rule, check, Item(1, Map(20)), Item(2, Map(21)), Item(3, Map(22)), Item(4, Title()))
            .ShouldHaveSingleItem().ShouldContain("the same map region", Case.Sensitive, "regions within 5° are the same background");

        // Kept: every scene's own picture is its own background, distant regions differ, one title card alone is fine.
        Run(rule, check, Item(1, new RenderPlan { Base = PlanBases.Image }), Item(2, new RenderPlan { Base = PlanBases.Image }),
            Item(3, new RenderPlan { Base = PlanBases.Image }), Item(4, Title())).ShouldBeEmpty();
        Run(rule, check, Item(1, Map(-100)), Item(2, Map(20)), Item(3, Map(100)), Item(4, Title())).ShouldBeEmpty();
        Run(rule, Check(2), Item(1, Title()), Item(2, Title())).ShouldBeEmpty("with fewer than three scenes the share is not checked");
        Run(rule, Check(3, pictures: false), Item(1, new RenderPlan { Base = PlanBases.Image }), Item(2, new RenderPlan { Base = "hologram" }), Item(3, Map()))
            .ShouldHaveSingleItem("an image base without a picture and an unknown base are drawn as title cards").ShouldContain("title cards");
    }

    [Fact]
    public void TwoBeatsInARowDoNotShowTheSameShot()
    {
        var rule = new RepeatedShotRule();
        var check = Check(2);
        Run(rule, check, Item(1, Map(20, Beat(1, "a", 1.5, Pin("a")), Beat(2, "a", 1.5))), Item(2, Title()))
            .ShouldHaveSingleItem().ShouldContain("scene 1, sentence 2: the beat repeats the previous shot");
        Run(rule, check, Item(1, Map(20, Beat(1))), Item(2, Map(20, Beat(1, actions: [Pin("b")]))))
            .ShouldHaveSingleItem().ShouldContain("scene 2 opens on the shot scene 1 ends on");

        // Kept: the second beat brings an action, or moves the camera; the next scene opens elsewhere.
        Run(rule, check, Item(1, Map(20, Beat(1, "a", 1.5, Pin("a")), Beat(2, "a", 1.5, Pin("b")))), Item(2, Title())).ShouldBeEmpty();
        Run(rule, check, Item(1, Map(20, Beat(1, "a", 1.5, Pin("a")), Beat(2, "b", 1.5))), Item(2, Map(20, Beat(1, "b", 2)))).ShouldBeEmpty();
        Run(rule, check, Item(1, Map(20, Beat(1))), Item(2, Map(100, Beat(1)))).ShouldBeEmpty();
    }

    [Fact]
    public void ASentenceHasAtMostOneBeatOfItsOwnScene()
    {
        var rule = new BeatsPerSentenceRule();
        var check = Check(1, sentences: 2);
        var found = Run(rule, check, Item(1, Map(20, Beat(1, actions: [Pin("a")]), Beat(1, actions: [Pin("b")]), Beat(5, actions: [Pin("a")]))));
        found.ShouldBe(["scene 1 has 2 beats for sentence 1 (at most 1)", "scene 1 has a beat for sentence 5 but only 2 sentences"]);
        Run(rule, check, Item(1, Map(20, Beat(1, actions: [Pin("a")]), Beat(2, actions: [Pin("b")])))).ShouldBeEmpty();
    }

    [Fact]
    public void ALabelHasAtMostThreeWords()
    {
        var rule = new LabelWordsRule();
        var plan = Map(20, Beat(1, actions: [Pin("a", "The old harbour of the city"), new PlanAction { Type = PlanActions.Text, Text = "A statement card may be long." }]));
        plan.Nodes = [new PlanNode { Id = "n", Label = "One two three four", X = 0.5, Y = 0.5 }];
        var found = Run(rule, Check(1), Item(1, plan));
        found.Count.ShouldBe(2);
        found.ShouldContain("scene 1: the label «One two three four» has 4 words (at most 3)");
        found.ShouldContain("scene 1: the label «The old harbour of the city» has 6 words (at most 3)");
        Run(rule, Check(1), Item(1, Map(20, Beat(1, actions: [Pin("a", "Old harbour")])))).ShouldBeEmpty("the text action is a statement, not a label");
        LabelWordsRule.Words("  Bir   iki üç ").ShouldBe(3);
    }

    [Fact]
    public void WithSubtitlesTheBottomQuarterHasNoText()
    {
        var rule = new SubtitleAreaRule();
        var plan = new RenderPlan
        {
            Base = PlanBases.Image,
            Anchors = [new PlanAnchor { Id = "boat", X = 0.4, Y = 0.9 }, new PlanAnchor { Id = "sand", X = 0.4, Y = 0.95 }, new PlanAnchor { Id = "sky", Label = "Sky", X = 0.5, Y = 0.2 }],
            Nodes = [new PlanNode { Id = "low", Label = "Low", X = 0.5, Y = 0.8 }],
            Beats = [Beat(1, actions: [Pin("boat"), new PlanAction { Type = PlanActions.Highlight, At = "sand" }])],
        };
        var found = Run(rule, Check(1), Item(1, plan));
        found.ShouldBe([
            "scene 1: the point «boat» with text is at y 0.9, in the bottom quarter kept for the subtitles (y at most 0.75)",
            "scene 1: the box «low» is at y 0.8, in the bottom quarter kept for the subtitles (y at most 0.75)",
        ], "a highlight shows no text, so the low point without a label is fine");
        Run(rule, Check(1, subtitles: false), Item(1, plan)).ShouldBeEmpty("without subtitles the bottom quarter is free");
    }

    [Fact]
    public void EveryActionPointsAtPointsOfItsOwnScene()
    {
        var rule = new ActionPointsRule();
        var first = Map(20, Beat(1, actions: [new PlanAction { Type = PlanActions.Arrow, From = "a", To = "c" }, Pin("b")]));
        var second = Map(40, Beat(1, actions: [new PlanAction { Type = PlanActions.Path, From = "a", To = "b", Via = ["x"] }, Pin(string.Empty)]));
        second.Anchors.Add(new PlanAnchor { Id = "c", Lon = 45, Lat = 40 });
        var found = Run(rule, Check(2), Item(1, first), Item(2, second));
        found.ShouldBe([
            "scene 1: the arrow action points at «c», which is not a point of that scene",
            "scene 2: the path action points at «x», which is not a point of that scene",
            "scene 2: the pin action has no point",
        ], "scene 2's point c does not count for scene 1");
        Run(rule, Check(1), Item(1, Map(20, Beat(1, actions: [new PlanAction { Type = PlanActions.Path, From = "a", To = "b" }, new PlanAction { Type = PlanActions.Counter, Value = 3 }]))))
            .ShouldBeEmpty("a counter without a point is fine");
    }

    [Fact]
    public void TheStructureComesFirstThenTheRulesInOrderAndARepairThatLosesASceneIsNotUsed()
    {
        PlanReader.Rules.Select(r => r.GetType()).ShouldBe([typeof(BackgroundShareRule), typeof(RepeatedShotRule), typeof(BeatsPerSentenceRule),
            typeof(LabelWordsRule), typeof(SubtitleAreaRule), typeof(ActionPointsRule), typeof(TempoTargetRule)]);
        var check = Check(3);
        var first = new[] { Item(1, Title()), Item(2, Title("Two")), Item(3, Map(20, Beat(1, actions: [Pin("a", "A very long label here")]))) };
        var problems = PlanReader.Problems(first, check);
        problems.Count.ShouldBe(3);
        problems[0].ShouldStartWith("one background (title cards) fills 67%");
        problems[1].ShouldStartWith("scene 2 opens on the shot scene 1 ends on", Case.Sensitive, "two title cards in a row are the same shot");
        problems[2].ShouldContain("has 5 words");
        PlanReader.Problems([first[0], first[1]], check)[0].ShouldBe("2 scene plans for 3 scenes", "the structure is named first");

        var lost = new[] { Item(1, Title()), Item(2, Map(-100)) };
        PlanReader.Better(lost, first, check).ShouldBeFalse("a lost scene weighs more than any rule");
        var kept = new[] { Item(1, Title()), Item(2, Map(-100)), Item(3, Map(20, Beat(1, actions: [Pin("a", "Harbour")]))) };
        PlanReader.Better(kept, first, check).ShouldBeTrue();
        PlanReader.Better([], first, check).ShouldBeFalse();
    }

    [Fact]
    public async Task TheFindingsGoIntoTheSingleRepairAndThereIsNeverASecond()
    {
        const string broken = """{"scenes":[{"scene":1,"base":"map","title":"","region":[20,30,45,45],"nodes":[],"links":[],"anchors":[{"id":"a","label":"The great city of old","find":"","lon":28,"lat":41,"x":null,"y":null}],"beats":[]}]}""";
        const string fixedAnswer = """{"scenes":[{"scene":1,"base":"map","title":"","region":[20,30,45,45],"nodes":[],"links":[],"anchors":[{"id":"a","label":"Old city","find":"","lon":28,"lat":41,"x":null,"y":null}],"beats":[]}]}""";

        // Repaired: two calls, the second carries the finding; the repaired plan is drawn.
        var chat = new FakeChat().Answer(_ => Answer(broken)).Answer(_ => Answer(fixedAnswer));
        var input = Input();
        await Planner(chat).PlanAsync(Request(input), Steps(), CancellationToken.None);
        chat.Requests.Count.ShouldBe(2);
        chat.Requests[1].User.ShouldContain("Your previous answer did not fit: scene 1: the label «The great city of old» has 5 words (at most 3)");
        input.Scenes[0].Plan.ShouldNotBeNull().Anchors.Single().Label.ShouldBe("Old city");

        // Still broken after the repair: no third call; the plan is used as far as it goes.
        chat = new FakeChat().Answer(_ => Answer(broken)).Answer(_ => Answer(broken));
        input = Input();
        await Planner(chat).PlanAsync(Request(input), Steps(), CancellationToken.None);
        chat.Requests.Count.ShouldBe(2);
        input.Scenes[0].Plan.ShouldNotBeNull().Base.ShouldBe(PlanBases.Map);

        // A plan that keeps every rule: one call, no repair.
        chat = new FakeChat().Answer(_ => Answer(fixedAnswer));
        await Planner(chat).PlanAsync(Request(Input()), Steps(), CancellationToken.None);
        chat.Requests.ShouldHaveSingleItem().System.ShouldContain("fills at most 40% of the video");
    }

    private static ChatResponse Answer(string text) => new(text, new ChatUsage(100, 0, 0, 100), null, "end_turn", "r");

    private static ScenePlanner Planner(FakeChat chat) => new(chat, new CostCalculator(), NullLogger<ScenePlanner>.Instance);

    private static VideoStepProgress Steps() => new([(VideoSteps.Code, 1)], null);

    private static RenderInput Input() => new()
    {
        Width = 1280, Height = 720, DurationSeconds = 6, Language = "en", Title = "Test",
        Scenes = [new RenderScene { Number = 1, StartSeconds = 0, DurationSeconds = 6, Visual = "a map", Caption = "The city stood by the sea." }],
    };

    private static PlanRequest Request(RenderInput input) => new(
        new ResolvedModel(new CatalogModel { Provider = "anthropic", Id = "test-model" }, new ApiKey { Secret = "fake-test-key-plan-0123" }, string.Empty),
        null, input, null, [], [], Path.GetTempPath(), Guid.NewGuid());
}
