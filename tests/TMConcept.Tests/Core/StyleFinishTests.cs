using System.Text.Json;
using Shouldly;
using TMConcept.Core.Plans;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.43 (F12): every drawing style has its known pitfalls and its finishing layer; the scene plan prompt lists the chosen style's
/// pitfalls; the layer reaches the drawer only when the template has it on (a template saved before has it off and draws no canvas); the
/// same seed gives the same layer.
/// </summary>
public sealed class StyleFinishTests
{
    private static readonly IReadOnlyList<PlanScene> Scenes = [new PlanScene(1, 6, "a map of the route", ["The caravan leaves Bukhara."], string.Empty)];

    [Fact]
    public void EveryStyleHasTwoToFourPitfallsAndAFinishingLayer()
    {
        DrawStyles.Choosable.Count().ShouldBe(13);
        foreach (var definition in DrawStyles.Choosable)
        {
            definition.Pitfalls.Count.ShouldBeInRange(2, 4, definition.Key);
            definition.Pitfalls.ShouldAllBe(p => p.Trim().Length > 10 && p.Length <= 200);
            definition.Finish.IsEmpty.ShouldBeFalse(definition.Key);
            new[] { definition.Finish.Tooth, definition.Finish.Grain, definition.Finish.Vignette, definition.Finish.Bloom, definition.Finish.Shadow,
                definition.Finish.Boil }.ShouldAllBe(v => v >= 0 && v <= 1);
        }

        var auto = DrawStyles.Of(DrawStyle.Auto);
        auto.Pitfalls.ShouldBeEmpty("the AI choice has no look of its own");
        auto.Finish.IsEmpty.ShouldBeTrue();
        DrawStyles.Of(DrawStyle.PaperCut).Finish.Effects.ShouldContain("shadow");
        DrawStyles.Of(DrawStyle.PaperCut).Finish.Effects.ShouldContain("boil");
        DrawStyles.Of(DrawStyle.Neon).Finish.Effects.ShouldContain("bloom");
        DrawStyles.Of(DrawStyle.PencilSketch).Finish.Effects.ShouldContain("tooth");
    }

    [Fact]
    public void ThePlanPromptListsTheChosenStylesPitfallsOnly()
    {
        var terminal = PlanPromptBuilder.User("Silk Road", "en", new VideoStyle { Name = "t", DrawStyle = DrawStyle.Terminal }, Scenes);
        terminal.ShouldContain("Known pitfalls of this look");
        foreach (var pitfall in DrawStyles.Of(DrawStyle.Terminal).Pitfalls)
        {
            terminal.ShouldContain(pitfall);
        }

        foreach (var pitfall in DrawStyles.Of(DrawStyle.Neon).Pitfalls)
        {
            terminal.ShouldNotContain(pitfall);
        }

        PlanPromptBuilder.User("Silk Road", "en", new VideoStyle { Name = "t", DrawStyle = DrawStyle.Auto }, Scenes).ShouldNotContain("Known pitfalls");
        PlanPromptBuilder.User("Silk Road", "en", null, Scenes).ShouldNotContain("Known pitfalls");
        PlanPromptBuilder.Version.ShouldBe("4", "3: transitions and eases; 4: the style's pitfalls");
    }

    [Fact]
    public void WithFinishingOffTheInputHasNoLayerAndTheDrawerNoCanvas()
    {
        var off = Input();
        RenderStyleBuilder.Apply(off, new VideoStyle { Name = "t", DrawStyle = DrawStyle.PaperCut });
        off.Style.ShouldNotBeNull().Finish.ShouldBeNull("a template saved before has finishing off");
        RenderInputBuilder.ToJson(off).ShouldNotContain("\"finish\"");

        var saved = JsonSerializer.Deserialize("{\"name\":\"old\",\"drawStyle\":3}", CoreJsonContext.Default.VideoStyle).ShouldNotBeNull();
        saved.Finish.ShouldBeFalse("a template saved before 0.1.43 keeps its videos as they were");

        var on = Input();
        RenderStyleBuilder.Apply(on, new VideoStyle { Name = "t", DrawStyle = DrawStyle.PaperCut, Finish = true });
        var layer = on.Style.ShouldNotBeNull().Finish.ShouldNotBeNull();
        layer.Shadow.ShouldBe(1);
        layer.Steps.Count.ShouldBe(RenderFinishBuilder.BoilSteps * 2);
        RenderInputBuilder.ToJson(on).ShouldContain("\"finish\"");

        var auto = Input();
        RenderStyleBuilder.Apply(auto, new VideoStyle { Name = "t", DrawStyle = DrawStyle.Auto, Finish = true });
        auto.Style.ShouldNotBeNull().Finish.ShouldBeNull("no preset chosen, no look to finish");

        // The drawer makes its one canvas inside finishLayer, which runs only when the input carries style.finish.
        var code = PlanDrawer.Code;
        code.ShouldContain("var finish = style.finish || null;");
        code.ShouldContain("if (finish) finishLayer();");
        var canvas = code.IndexOf("el(\"canvas\"", StringComparison.Ordinal);
        canvas.ShouldBeGreaterThan(code.IndexOf("function finishLayer()", StringComparison.Ordinal));
        code.IndexOf("el(\"canvas\"", canvas + 1, StringComparison.Ordinal).ShouldBe(-1, "one canvas");
        code.Split("finishLayer()").Length.ShouldBe(3, "defined once, called once");
        PlanDrawer.IsDrawer(code).ShouldBeTrue("the drawer's first line stays the same, so kept videos are drawn again by it");
    }

    [Fact]
    public void TheSameSeedGivesTheSameLayer()
    {
        var finish = DrawStyles.Of(DrawStyle.PaperCut).Finish;
        var a = RenderFinishBuilder.Build(finish, "paper-cut|Silk Road").ShouldNotBeNull();
        var b = RenderFinishBuilder.Build(finish, "paper-cut|Silk Road").ShouldNotBeNull();
        var c = RenderFinishBuilder.Build(finish, "paper-cut|Byzantium").ShouldNotBeNull();
        JsonSerializer.Serialize(a, CoreJsonContext.Default.RenderFinish).ShouldBe(JsonSerializer.Serialize(b, CoreJsonContext.Default.RenderFinish));
        a.Seed.ShouldBe(RenderFinishBuilder.Seed("paper-cut|Silk Road"));
        a.Seed.ShouldNotBe(c.Seed);
        a.Steps.ShouldNotBe(c.Steps);
        a.Steps.ShouldAllBe(v => v >= -1 && v <= 1);
        a.Seed.ShouldBeGreaterThan(0);
        RenderFinishBuilder.Seed(string.Empty).ShouldBeGreaterThan(0);

        var first = Input();
        var second = Input();
        var style = new VideoStyle { Name = "t", DrawStyle = DrawStyle.Watercolor, Finish = true };
        RenderStyleBuilder.Apply(first, style);
        RenderStyleBuilder.Apply(second, style);
        RenderInputBuilder.ToJson(first).ShouldBe(RenderInputBuilder.ToJson(second), "the same video gets the same layer");
        first.Style!.Finish!.Steps.ShouldBeEmpty("watercolour has no boil");
        RenderFinishBuilder.Build(StyleFinish.None, "x").ShouldBeNull();
    }

    private static RenderInput Input() => new() { Title = "Silk Road", Language = "en", Width = 1920, Height = 1080, Fps = 30, DurationSeconds = 6 };
}
