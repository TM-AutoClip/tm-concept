using System.Text.Json;
using Shouldly;
using TMConcept.Core.Plans;
using TMConcept.Core.Render;
using TMConcept.Core.Scenes;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.42 (F1, F9, S4): the drawer's transition catalog chosen by the tempo's energy, one direction in a film, the camera's eases with a
/// fallback, the tempo targets in the plan rules, the plan format's new fields (and plans made before, which keep drawing as they did) and
/// the tight tempo's directed drift.
/// </summary>
public sealed class SceneTransitionTests
{
    private static List<PlanItem> Plans(params string[] transitions) =>
        transitions.Select((t, i) => new PlanItem { Scene = i + 1, Plan = new RenderPlan { Base = PlanBases.Title, Transition = t } }).ToList();

    private static string[] None(int count) => Enumerable.Repeat(SceneTransitions.None, count).ToArray();

    [Fact]
    public void TheTempoSetsTheEnergyAndTheEnergyTheTransitionsAllowed()
    {
        TransitionCatalog.EnergyOf(NarrationTempo.Relaxed).ShouldBe(TransitionEnergy.Calm);
        TransitionCatalog.EnergyOf(NarrationTempo.Natural).ShouldBe(TransitionEnergy.Medium);
        TransitionCatalog.EnergyOf(NarrationTempo.Tight).ShouldBe(TransitionEnergy.High);

        var calm = TransitionCatalog.Names(TransitionEnergy.Calm);
        calm.ShouldContain(SceneTransitions.Crossfade);
        calm.ShouldContain("slide-left");
        calm.ShouldNotContain("push-left");
        calm.ShouldNotContain(SceneTransitions.ZoomThrough);
        var high = TransitionCatalog.Names(TransitionEnergy.High);
        high.ShouldContain(SceneTransitions.ZoomThrough);
        high.ShouldContain("push-down");
        high.ShouldNotContain(SceneTransitions.Crossfade);

        TransitionCatalog.Find("Push-Up").ShouldNotBeNull().Direction.ShouldBe(SceneTransitions.Up);
        TransitionCatalog.Find("push").ShouldBeNull("a directional family needs its direction");
        TransitionCatalog.Find("crossfade-left").ShouldBeNull("a family without direction takes none");
        TransitionCatalog.Find("spin").ShouldBeNull();
        TransitionCatalog.Seconds(SceneTransitions.Push, TransitionEnergy.High).ShouldBe(SceneTransitions.HighSeconds);
        TransitionCatalog.Seconds(SceneTransitions.Slide, TransitionEnergy.Calm).ShouldBe(SceneTransitions.CalmSeconds);
        TransitionCatalog.Seconds(SceneTransitions.Cut, TransitionEnergy.Medium).ShouldBe(0);
    }

    [Fact]
    public void ThePlansChoiceIsKeptWhenItFitsTheEnergyOtherwiseTheDefaultOfTheEnergyOrTheStyle()
    {
        var tight = Plans("", "zoom-through", "crossfade", "spin");
        PlanMotion.Apply(tight, None(4), NarrationTempo.Tight, string.Empty);
        tight.Select(p => p.Plan.Transition).ShouldBe(["", "zoom-through", "push-left", "push-left"], "a calm crossfade does not fit a tight video");
        tight[1].Plan.TransitionSeconds.ShouldBe(SceneTransitions.ZoomThroughSeconds);
        tight[2].Plan.TransitionSeconds.ShouldBe(SceneTransitions.HighSeconds);
        tight[0].Plan.TransitionSeconds.ShouldBe(0, "the first scene has no transition");

        var natural = Plans("push-left", "", "crossfade");
        PlanMotion.Apply(natural, None(3), NarrationTempo.Natural, string.Empty);
        natural.Select(p => p.Plan.Transition).ShouldBe(["", "slide-left", "crossfade"]);

        var relaxed = Plans("", "zoom-through");
        PlanMotion.Apply(relaxed, None(2), NarrationTempo.Relaxed, string.Empty);
        relaxed[1].Plan.Transition.ShouldBe(SceneTransitions.Crossfade);

        // A style with a family uses only it (a terminal cuts, paper slides); a family that does not fit the energy leaves the tempo's own.
        var terminal = Plans("", "push-left", "zoom-through");
        PlanMotion.Apply(terminal, None(3), NarrationTempo.Tight, DrawStyles.Of(DrawStyle.Terminal).Transition);
        terminal.Skip(1).Select(p => p.Plan.Transition).ShouldBe([SceneTransitions.Cut, SceneTransitions.Cut]);
        var paper = Plans("", "crossfade");
        PlanMotion.Apply(paper, None(2), NarrationTempo.Natural, DrawStyles.Of(DrawStyle.PaperCut).Transition);
        paper[1].Plan.Transition.ShouldBe("slide-left");
        var board = Plans("", "");
        PlanMotion.Apply(board, None(2), NarrationTempo.Relaxed, DrawStyles.Of(DrawStyle.Blueprint).Transition);
        board[1].Plan.Transition.ShouldBe(SceneTransitions.Crossfade, "a wipe is not calm");

        // A placeholder of the text wins: its scene gets no transition of the plan.
        var asked = Plans("", "push-left", "push-left");
        PlanMotion.Apply(asked, ["", SceneTransitions.Fade, ""], NarrationTempo.Tight, string.Empty);
        asked.Select(p => p.Plan.Transition).ShouldBe(["", "", "push-left"]);
    }

    [Fact]
    public void EveryDirectionalTransitionOfAFilmGoesTheOneDirection()
    {
        var plans = Plans("", "push-right", "slide-left", "wipe-right", "zoom-through");
        PlanMotion.Apply(plans, None(5), NarrationTempo.Tight, string.Empty);
        plans.Select(p => p.Plan.Transition).ShouldBe(["", "push-right", "slide-right", "wipe-right", "zoom-through"],
            "the way a scene leaves is the way the next one enters");

        PlanMotion.Direction(["slide-up", "push-down"]).ShouldBe(SceneTransitions.Up, "on a tie the first named wins");
        PlanMotion.Direction(["crossfade", "", "spin-left"]).ShouldBe(SceneTransitions.Left, "the default direction");

        var defaults = Plans("", "", "");
        PlanMotion.Apply(defaults, None(3), NarrationTempo.Natural, string.Empty);
        defaults.Skip(1).Select(p => p.Plan.Transition).Distinct().ShouldHaveSingleItem().ShouldBe("slide-left");
    }

    [Fact]
    public void AnUnknownEaseIsRefusedAndFallsBackToTheTemposDefault()
    {
        PlanMotion.Ease("bounce.out", NarrationTempos.Targets(NarrationTempo.Tight)).ShouldBe("expo.out", "a camera never bounces");
        PlanMotion.Ease("power9.in", NarrationTempos.Targets(NarrationTempo.Natural)).ShouldBe(PlanEases.Classic);
        PlanMotion.Ease("", NarrationTempos.Targets(NarrationTempo.Relaxed)).ShouldBe("sine.inOut");
        PlanMotion.Ease(" Back.OUT ", NarrationTempos.Targets(NarrationTempo.Relaxed)).ShouldBe("back.out", "written as GSAP spells it");
        NarrationTempos.Targets(NarrationTempo.Natural).DefaultEase.ShouldBe(PlanEases.Classic, "natural moves as every camera did before");

        var plans = Plans("", "");
        plans[1].Plan.Beats = [new PlanBeat { Sentence = 1, Camera = new PlanCamera { Zoom = 1.5, Ease = "elastic.out" } }, new PlanBeat { Sentence = 2 }];
        PlanMotion.Apply(plans, None(2), NarrationTempo.Tight, string.Empty);
        plans[1].Plan.Beats[0].Camera!.Ease.ShouldBe("expo.out");
        plans[1].Plan.Beats[1].Camera.ShouldBeNull("a beat without a camera gets none");
    }

    [Fact]
    public void OnlyTheTightTempoMovesAStillPictureTheFilmsWay()
    {
        var tight = Plans("", "wipe-down");
        PlanMotion.Apply(tight, None(2), NarrationTempo.Tight, string.Empty);
        tight.Select(p => p.Plan.Drift).ShouldBe([SceneTransitions.Down, SceneTransitions.Down]);

        var through = Plans("", "zoom-through");
        PlanMotion.Apply(through, None(2), NarrationTempo.Tight, string.Empty);
        through.Select(p => p.Plan.Drift).ShouldBe([PlanDrifts.In, PlanDrifts.In], "no directional transition: a push in");

        foreach (var tempo in new[] { NarrationTempo.Natural, NarrationTempo.Relaxed })
        {
            var plans = Plans("", "slide-left");
            PlanMotion.Apply(plans, None(2), tempo, string.Empty);
            plans.Select(p => p.Plan.Drift).ShouldBe([PlanDrifts.Zoom, PlanDrifts.Zoom], "natural and relaxed keep the slight zoom");
        }
    }

    [Fact]
    public void ThePlanFormatAsksForTheNewFieldsAndAnAnswerWithoutThemReadsAsBefore()
    {
        var schema = PlanPromptBuilder.Schema();
        var scene = schema["properties"]!["scenes"]!["items"]!;
        scene["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldContain("transition");
        scene["properties"]!["beats"]!["items"]!["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldContain("ease");

        var answer = """{"scenes":[{"scene":1,"base":"title","title":"A","transition":"","beats":[]},{"scene":2,"base":"title","title":"B","transition":"PUSH-LEFT","beats":[{"sentence":1,"focus":"","zoom":1.4,"ease":"back.out","actions":[]}]}]}""";
        var items = PlanReader.Read(answer);
        items[1].Plan.Transition.ShouldBe("push-left");
        items[1].Plan.Beats[0].Camera.ShouldNotBeNull().Ease.ShouldBe("back.out");

        var old = PlanReader.Read("""{"scenes":[{"scene":1,"base":"title","title":"A","beats":[{"sentence":1,"focus":"","zoom":1.4,"actions":[]}]}]}""");
        old[0].Plan.Transition.ShouldBeEmpty();
        old[0].Plan.Beats[0].Camera!.Ease.ShouldBeEmpty();
        PlanReader.Clean(old, [1], [false])[0].Plan.Transition.ShouldBeEmpty("cleaning leaves the motion to PlanMotion");

        // A kept input of a video made before 0.1.42 has no transition, seconds, drift or ease: the drawer draws it as it did.
        var input = JsonSerializer.Deserialize("""{"scenes":[{"number":2,"plan":{"base":"title","beats":[{"sentence":1,"camera":{"focus":"","zoom":1.4}}]}}]}""",
            CoreJsonContext.Default.RenderInput)!;
        var plan = input.Scenes[0].Plan.ShouldNotBeNull();
        (plan.Transition, plan.TransitionSeconds, plan.Drift, plan.Beats[0].Camera!.Ease).ShouldBe((string.Empty, 0d, string.Empty, string.Empty));
    }

    [Fact]
    public void ThePromptNamesTheEnergysTransitionsTheEasesAndTheTargets()
    {
        var tight = PlanPromptBuilder.System(MotionIntensity.Normal, NarrationTempo.Tight);
        tight.ShouldContain("zoom-through");
        tight.ShouldContain("Tempo: tight");
        tight.ShouldContain("expo.out or back.out");
        tight.ShouldContain("power3.inOut", Case.Sensitive, "the allowed eases are listed");
        tight.ShouldNotContain("bounce");
        var relaxed = PlanPromptBuilder.System(MotionIntensity.Calm, NarrationTempo.Relaxed);
        relaxed.ShouldNotContain("zoom-through");
        relaxed.ShouldContain("sine.inOut");
        PlanPromptBuilder.System(MotionIntensity.Normal, NarrationTempo.Tight, SceneTransitions.Cut).ShouldContain("one of: cut. This look uses only these");
        PlanPromptBuilder.System(MotionIntensity.Normal).ShouldContain("Tempo: natural", Case.Sensitive, "a video without a template is natural");
        PlanDrawer.Code.ShouldContain("plan.transition");
        PlanDrawer.Code.ShouldContain("cam.ease || \"power2.inOut\"", Case.Sensitive, "a plan without an ease moves as before");
    }

    [Fact]
    public void TheTightAndRelaxedTargetsAreCheckedTheNaturalOneIsNot()
    {
        var scenes = Enumerable.Range(1, 3).Select(n => new PlanScene(n, 10, "v", ["One.", "Two.", "Three.", "Four."], string.Empty)).ToList();
        var pictures = Enumerable.Repeat(false, 3).ToList();
        var bare = Enumerable.Range(1, 3).Select(n => new PlanItem
        {
            Scene = n,
            Plan = new RenderPlan { Base = PlanBases.Title, Title = "T" + n, Beats = [new PlanBeat { Sentence = 1, Actions = [new PlanAction { Type = PlanActions.Text, Text = "x" }] }] },
        }).ToList();
        var rule = new TempoTargetRule();
        rule.Problems(bare, new PlanCheck(scenes, pictures, true, NarrationTempo.Tight)).ShouldContain(p => p.Contains("the plan has 3", StringComparison.Ordinal));
        rule.Problems(bare, new PlanCheck(scenes, pictures, true, NarrationTempo.Natural)).ShouldBeEmpty();
        rule.Problems(bare, new PlanCheck(scenes, pictures, true)).ShouldBeEmpty("a check without a tempo is natural, as before");

        var busy = Enumerable.Range(1, 3).Select(n => new PlanItem
        {
            Scene = n,
            Plan = new RenderPlan
            {
                Base = PlanBases.Title,
                Title = "T" + n,
                Beats = [.. Enumerable.Range(1, 4).Select(s => new PlanBeat { Sentence = s, Camera = new PlanCamera { Zoom = 1 + (s / 10.0) } })],
            },
        }).ToList();
        rule.Problems(busy, new PlanCheck(scenes, pictures, true, NarrationTempo.Tight)).ShouldBeEmpty("12 changes in 30 s, every one with the camera");
        rule.Problems(busy, new PlanCheck(scenes, pictures, true, NarrationTempo.Relaxed)).ShouldHaveSingleItem().ShouldContain("leave more sentences without a beat");
        rule.Problems(bare, new PlanCheck(scenes, pictures, true, NarrationTempo.Relaxed)).ShouldBeEmpty();
    }
}
