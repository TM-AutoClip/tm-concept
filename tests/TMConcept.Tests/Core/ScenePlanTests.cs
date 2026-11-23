using System.Text.Json;
using Shouldly;
using TMConcept.Core.Engines;
using TMConcept.Core.Plans;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Engines.Engines;

namespace TMConcept.Tests.Core;

/// <summary>0.1.33: the app's drawer — the scene plan prompt, its reading and cleaning, its timing, the image points and the drawer files.</summary>
public sealed class ScenePlanTests
{
    private static string Action(string type, string at = "", string from = "", string to = "", string country = "") =>
        $$"""{"type":"{{type}}","from":"{{from}}","to":"{{to}}","via":[],"at":"{{at}}","label":"","text":"","walker":"","dashed":false,"country":"{{country}}","size":0,"fromValue":null,"value":null,"suffix":""}""";

    [Fact]
    public void ThePromptFollowsTheMotionAndListsEverySentenceAndPicture()
    {
        PlanPromptBuilder.System(MotionIntensity.Calm).ShouldContain("Motion: calm");
        PlanPromptBuilder.System(MotionIntensity.Lively).ShouldContain("walkers travelling");
        PlanPromptBuilder.System(MotionIntensity.Normal).ShouldContain("no walkers");
        var user = PlanPromptBuilder.User("Greek fire", "tr", new VideoStyle { DrawStyle = DrawStyle.Blueprint },
            [new PlanScene(1, 6.2, "a map", ["İlk cümle.", "İkinci «cümle»."], "a fleet in the lower left"), new PlanScene(2, 4, "ships", ["Son."], "")]);
        user.ShouldContain("language Turkish (tr)");
        user.ShouldContain("Scene 1 (6.2 s): picture «a fleet in the lower left»");
        user.ShouldContain("1) «İlk cümle.» 2) «İkinci \"cümle\".»", Case.Sensitive, "guillemets inside the data cannot close it");
        user.ShouldContain("Scene 2 (4 s): no picture");
        user.ShouldContain(DrawStyles.Of(DrawStyle.Blueprint).ImagePrompt);
        PlanPromptBuilder.Schema()["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["scenes"]);
    }

    [Fact]
    public void AnAnswerIsCleanedUnknownBasesAndActionsAndMissingPointsAreDropped()
    {
        var answer = "```json\n{\"scenes\":[" +
            "{\"scene\":1,\"base\":\"image\",\"anchors\":[{\"id\":\"bridge\",\"label\":\"Köprü\",\"find\":\"the bridge\",\"x\":0.3,\"y\":0.95},{\"id\":\"ghost\"}]," +
            "\"beats\":[{\"sentence\":9,\"focus\":\"nowhere\",\"zoom\":7,\"actions\":[" + Action("pin", at: "bridge") + "," + Action("arrow", from: "bridge", to: "ghost") + "," + Action("dance") + "]}," +
            "{\"sentence\":2,\"focus\":\"bridge\",\"zoom\":1.5,\"actions\":[" + Action("highlight", at: "bridge") + "]}]}," +
            "{\"scene\":2,\"base\":\"hologram\",\"title\":\"Sonuç\",\"beats\":[]}," +
            "{\"scene\":3,\"base\":\"map\",\"anchors\":[{\"id\":\"a\",\"lon\":28,\"lat\":41},{\"id\":\"b\",\"lon\":33,\"lat\":39}],\"beats\":[{\"sentence\":1,\"actions\":[" +
            Action("path", from: "a", to: "b") + "," + Action("country", country: "tur") + "]}]}]}\n```";
        var items = PlanReader.Read(answer);
        items.Count.ShouldBe(3);
        PlanReader.Problems(items, 4).ShouldBe(["3 scene plans for 4 scenes", "scene 4 has no plan"]);
        PlanReader.Problems(items, 3).ShouldBeEmpty();

        var plans = PlanReader.Clean(items, [2, 1, 1, 1], [true, false, false, true]);
        plans.Count.ShouldBe(4);
        var first = plans[0].Plan;
        first.Anchors.Select(a => a.Id).ShouldBe(["bridge"], "a point without coordinates is dropped");
        first.Anchors[0].Y.ShouldBe(0.78, "kept above the subtitles");
        first.Beats.Select(b => b.Sentence).ShouldBe([2], "sentence 9 of a two-sentence scene is its last; the two beats of it join");
        first.Beats[0].Actions.Select(a => a.Type).ShouldBe(["pin", "highlight"], "an arrow to a missing point and an unknown action are dropped");
        first.Beats[0].Camera.ShouldNotBeNull().Focus.ShouldBeEmpty();
        first.Beats[0].Camera!.Zoom.ShouldBe(2.2);
        plans[1].Plan.Base.ShouldBe(PlanBases.Title, "an unknown base without a picture is a title");
        plans[1].Plan.Title.ShouldBe("Sonuç");
        var map = plans[2].Plan;
        map.Map.ShouldNotBeNull().Region.ShouldBe([24, 35, 37, 45], "a map without a region gets one around its places");
        map.Beats[0].Actions.Select(a => a.Type).ShouldBe(["path", "country"]);
        map.Beats[0].Actions[1].Country.ShouldBe("TUR");
        plans[3].Plan.Base.ShouldBe(PlanBases.Image, "a scene left out shows its picture");
        plans[3].Plan.Beats.ShouldBeEmpty();
        PlanReader.Read("no json").ShouldBeEmpty();
    }

    [Fact]
    public void BeatsFollowTheSpokenSentencesOrTheLettersOfASilentScene()
    {
        var plan = new RenderPlan { Beats = [new PlanBeat { Sentence = 1 }, new PlanBeat { Sentence = 2 }] };
        var spans = new[] { new SpeechSpan(0, 2000, "Bir."), new SpeechSpan(2300, 1500, "İki.") };
        var sentences = PlanTiming.Sentences("ignored", spans);
        sentences.ShouldBe(["Bir.", "İki."]);
        PlanTiming.Apply(plan, 10, 5, PlanTiming.Starts(sentences, spans, 5));
        plan.Beats.Select(b => (b.Start, b.Duration)).ShouldBe([(10, 2.3), (12.3, 2.7)]);

        var silent = PlanTiming.Sentences("Kısa. Bu cümle daha da uzun.", null);
        silent.Count.ShouldBe(2);
        var starts = PlanTiming.Starts(silent, null, 9);
        starts[0].ShouldBe(0);
        starts[1].ShouldBeGreaterThan(0.5);
        starts[1].ShouldBeLessThan(4.5, "the shorter first sentence takes the smaller share");
    }

    [Fact]
    public void ANewVersionCarriesThePlansAndTimesThemAgain()
    {
        var source = new RenderInput { Scenes = [new RenderScene { Number = 1, Caption = "Bir. İki.", Plan = new RenderPlan { Beats = [new PlanBeat { Sentence = 2 }] } }] };
        var timed = new RenderInput { Scenes = [new RenderScene { Number = 1, Caption = "Bir. İki.", StartSeconds = 0, DurationSeconds = 8 }] };
        PlanTiming.Carry(source, timed, [[new SpeechSpan(0, 3000, "Bir."), new SpeechSpan(5000, 2000, "İki.")]]);
        timed.Scenes[0].Plan.ShouldNotBeNull().Beats[0].Start.ShouldBe(5);
        timed.Scenes[0].Plan!.Beats[0].Duration.ShouldBe(3);
    }

    [Fact]
    public void ImagePointsMoveTheFoundAnchorsOnly()
    {
        var anchors = new List<PlanAnchor>
        {
            new() { Id = "a", Find = "the bridge", X = 0.1, Y = 0.1 },
            new() { Id = "b", Find = "the tower", X = 0.2, Y = 0.2 },
            new() { Id = "c", Find = "a cat", X = 0.3, Y = 0.3 },
        };
        ImagePointPrompt.User(anchors).ShouldContain("- b: «the tower»");
        var moved = ImagePointPrompt.Apply(anchors, """{"points":[{"id":"a","x":0.62,"y":0.4,"found":true},{"id":"b","x":55,"y":90,"found":true},{"id":"c","x":0.9,"y":0.9,"found":false}]}""");
        moved.ShouldBe(2);
        (anchors[0].X, anchors[0].Y).ShouldBe((0.62, 0.4));
        (anchors[1].X, anchors[1].Y).ShouldBe((0.55, 0.78), "a percentage is read as one and kept above the subtitles");
        (anchors[2].X, anchors[2].Y).ShouldBe((0.3, 0.3), "not found: the plan's guess stays");
        ImagePointPrompt.Apply(anchors, "I cannot see").ShouldBe(0);
    }

    [Fact]
    public void TheDrawerPassesTheSafetyCheckAndTheWorldMapIsWrittenOnlyForMapScenes()
    {
        EngineCodeCheck.Check(EngineKind.HyperFrames, PlanDrawer.Code).ShouldBeEmpty("the drawer is written through the same check as a model's code");
        PlanDrawer.IsDrawer(PlanDrawer.Code).ShouldBeTrue();
        PlanDrawer.IsDrawer("window.x = 1;").ShouldBeFalse();
        var world = PlanDrawer.WorldScript;
        world.ShouldStartWith("window.TM_WORLD = ");
        using (var doc = JsonDocument.Parse(world["window.TM_WORLD = ".Length..].TrimEnd().TrimEnd(';')))
        {
            var countries = doc.RootElement.GetProperty("countries");
            countries.GetArrayLength().ShouldBeGreaterThan(170);
            countries.EnumerateArray().ShouldContain(c => c.GetProperty("c").GetString() == "TUR");
        }

        PlanDrawer.Draws(EngineKind.HyperFrames, new VideoStyle { Drawer = SceneDrawer.App }).ShouldBeTrue();
        PlanDrawer.Draws(EngineKind.Remotion, new VideoStyle { Drawer = SceneDrawer.App }).ShouldBeFalse("the drawer is a HyperFrames page");
        PlanDrawer.Draws(EngineKind.HyperFrames, new VideoStyle()).ShouldBeFalse("a template saved before keeps the model's code");
        PlanDrawer.Draws(EngineKind.HyperFrames, null).ShouldBeFalse("the drawer stands on the template helper");

        var input = new RenderInput { Width = 1080, Height = 1920, Scenes = [new RenderScene { Number = 1, Plan = new RenderPlan { Base = PlanBases.Diagram } }] };
        HyperFramesEngine.IndexHtml(input).ShouldNotContain(PlanDrawer.WorldFile);
        input.Scenes[0].Plan!.Base = PlanBases.Map;
        HyperFramesEngine.IndexHtml(input).ShouldContain("<script src=\"world.js\"></script>");
        RenderInputBuilder.ToJson(new RenderInput { Scenes = [new RenderScene()] }).ShouldNotContain("\"plan\"", Case.Sensitive, "a model's code gets no empty plans");
    }
}
