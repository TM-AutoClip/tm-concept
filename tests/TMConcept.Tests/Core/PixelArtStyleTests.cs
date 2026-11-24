using System.Text.Json;
using Shouldly;
using TMConcept.Core.Images;
using TMConcept.Core.Plans;
using TMConcept.Core.Render;
using TMConcept.Core.Scenes;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.51 (F13): pixel art, the 15th choice: added at the end of the stored numbers, a full definition with its own transition family
/// (dissolve, iris, blocks) and no finishing layer, its rules and pitfalls in the scene plan prompt, its pixel layer in the input and the
/// drawer, and transitions that never go through black (the motion check reports any black frame inside the film).
/// </summary>
public sealed class PixelArtStyleTests
{
    private static readonly IReadOnlyList<PlanScene> Scenes = [new PlanScene(1, 6, "the castle at dawn", ["The siege begins."], string.Empty)];

    private static List<PlanItem> Plans(params string[] transitions) =>
        transitions.Select((t, i) => new PlanItem { Scene = i + 1, Plan = new RenderPlan { Base = PlanBases.Title, Transition = t } }).ToList();

    private static string[] None(int count) => Enumerable.Repeat(SceneTransitions.None, count).ToArray();

    private static IReadOnlyList<string> Pixel => DrawStyles.Of(DrawStyle.PixelArt).Families;

    [Fact]
    public void PixelArtIsAddedAtTheEndAndTheStoredNumbersOfTheOtherStylesStay()
    {
        var expected = new Dictionary<DrawStyle, int>
        {
            [DrawStyle.Flat] = 0, [DrawStyle.StickFigure] = 1, [DrawStyle.LineArt] = 2, [DrawStyle.PaperCut] = 3, [DrawStyle.Infographic] = 4,
            [DrawStyle.Neon] = 5, [DrawStyle.PencilSketch] = 6, [DrawStyle.Cartoon] = 7, [DrawStyle.Blueprint] = 8, [DrawStyle.Hologram] = 9,
            [DrawStyle.Terminal] = 10, [DrawStyle.Isometric] = 11, [DrawStyle.Watercolor] = 12, [DrawStyle.Auto] = 13, [DrawStyle.PixelArt] = 14,
        };
        Enum.GetValues<DrawStyle>().Length.ShouldBe(expected.Count, "15 choices: 14 looks and the AI choice");
        foreach (var (style, number) in expected)
        {
            ((int)style).ShouldBe(number, style.ToString());
        }

        Enum.GetValues<DrawStyle>().Max().ShouldBe(DrawStyle.PixelArt, "a new value goes to the end");

        // Templates stored as JSON numbers read as they were.
        JsonSerializer.Deserialize("{\"name\":\"old\",\"drawStyle\":12}", CoreJsonContext.Default.VideoStyle)!.DrawStyle.ShouldBe(DrawStyle.Watercolor);
        JsonSerializer.Deserialize("{\"name\":\"old\",\"drawStyle\":13}", CoreJsonContext.Default.VideoStyle)!.DrawStyle.ShouldBe(DrawStyle.Auto);
        JsonSerializer.Deserialize("{\"name\":\"new\",\"drawStyle\":14}", CoreJsonContext.Default.VideoStyle)!.DrawStyle.ShouldBe(DrawStyle.PixelArt);
    }

    [Fact]
    public void TheDefinitionIsFullWithItsOwnTransitionsAndNoFinishingLayer()
    {
        var pixel = DrawStyles.Of(DrawStyle.PixelArt);
        pixel.Key.ShouldBe("pixel-art");
        pixel.ImagePrompt.ShouldContain("pixel art");
        pixel.Drawing.ShouldContain("480 × 270");
        pixel.Craft.ShouldContain("never through black");
        pixel.Texture.ShouldBe(StyleTexture.None);
        pixel.Finish.IsEmpty.ShouldBeTrue("no finishing layer: it would blur the pixels");
        pixel.PrefersImages.ShouldBeFalse("it draws its own ground when the image source is off");
        pixel.Families.ShouldBe([SceneTransitions.Dissolve, SceneTransitions.Iris, SceneTransitions.Blocks]);
        pixel.Pitfalls.Count.ShouldBeInRange(2, 4);
        pixel.Pitfalls.ShouldContain(p => p.Contains("three or more frames", StringComparison.Ordinal) && p.Contains("blink", StringComparison.Ordinal));
        DrawStyles.FindKey("Pixel-Art")!.Style.ShouldBe(DrawStyle.PixelArt);
        DrawStyles.Choosable.ShouldContain(pixel, "the AI choice may pick it");
        DrawStyles.All[^1].ShouldBe(pixel, "the editor lists it last");

        // The other styles keep their single family (or none).
        DrawStyles.Of(DrawStyle.Terminal).Families.ShouldBe([SceneTransitions.Cut]);
        DrawStyles.Of(DrawStyle.Flat).Families.ShouldBeEmpty();
        PixelArtLimits.MaxDarkFrames.ShouldBeLessThan(3);
    }

    [Fact]
    public void TheAiChoiceListsPixelArtAndReadsItBack()
    {
        ImagePromptBuilder.User("Byzantium", ["a map"], DrawStyle.Auto).ShouldContain("pixel-art");
        ImagePromptBuilder.ReadStyle("""{"prompts":[{"scene":1,"prompt":"p"}],"style":"pixel-art"}""").Preset.ShouldBe(DrawStyle.PixelArt);
        DrawStyles.ConceptLine(DrawStyle.PixelArt, "en").ShouldContain("pixel art");
    }

    [Fact]
    public void ThePixelTransitionsAreOfferedOnlyToPixelArt()
    {
        foreach (var energy in Enum.GetValues<TransitionEnergy>())
        {
            var names = TransitionCatalog.Names(energy);
            names.ShouldNotContain(SceneTransitions.Dissolve);
            names.ShouldNotContain(SceneTransitions.Iris);
            names.ShouldNotContain(n => n.StartsWith("blocks", StringComparison.Ordinal));
            TransitionCatalog.StyleFamilies(Pixel, energy).ShouldNotBeEmpty($"pixel art has a transition at {energy}");
        }

        TransitionCatalog.Find("blocks-up").ShouldNotBeNull().Direction.ShouldBe(SceneTransitions.Up);
        TransitionCatalog.Find("blocks").ShouldBeNull("blocks go in a direction");
        TransitionCatalog.Find("dissolve").ShouldNotBeNull().Definition.StyleOnly.ShouldBeTrue();
        TransitionCatalog.DefaultFamily(TransitionEnergy.High, Pixel).ShouldBe(SceneTransitions.Dissolve);

        var natural = PlanPromptBuilder.System(MotionIntensity.Normal, NarrationTempo.Natural, Pixel);
        natural.ShouldContain("one of: dissolve, iris, blocks-left, blocks-right, blocks-up, blocks-down. This look uses only these");
        PlanPromptBuilder.System(MotionIntensity.Normal, NarrationTempo.Relaxed, Pixel).ShouldContain("one of: dissolve. This look uses only these");
        var plain = PlanPromptBuilder.System(MotionIntensity.Normal, NarrationTempo.Natural);
        plain.ShouldNotContain("iris");
        plain.ShouldNotContain("blocks-");
        plain.ShouldNotContain(" dissolve,", Case.Sensitive, "blur-dissolve stays, the pixel dissolve is not offered");
        PlanPromptBuilder.System(MotionIntensity.Normal, NarrationTempo.Natural, DrawStyles.Of(DrawStyle.PaperCut).Families)
            .ShouldBe(PlanPromptBuilder.System(MotionIntensity.Normal, NarrationTempo.Natural, SceneTransitions.Slide), "a single family reads as before");
    }

    [Fact]
    public void TheMotionKeepsAPixelTransitionOnlyForPixelArt()
    {
        var pixel = Plans("", "blocks-right", "push-left", "iris");
        PlanMotion.Apply(pixel, None(4), NarrationTempo.Natural, Pixel);
        pixel.Select(p => p.Plan.Transition).ShouldBe(["", "blocks-right", SceneTransitions.Dissolve, SceneTransitions.Iris],
            "a transition of another family becomes the style's first");
        pixel[1].Plan.TransitionSeconds.ShouldBe(SceneTransitions.MediumSeconds);

        var calm = Plans("", "iris");
        PlanMotion.Apply(calm, None(2), NarrationTempo.Relaxed, Pixel);
        calm[1].Plan.Transition.ShouldBe(SceneTransitions.Dissolve, "an iris is not calm");

        var other = Plans("", "iris", "dissolve", "blocks-left");
        PlanMotion.Apply(other, None(4), NarrationTempo.Natural, string.Empty);
        other.Skip(1).Select(p => p.Plan.Transition).ShouldAllBe(t => t == "slide-left", "a video without pixel art never gets a pixel transition");
    }

    [Fact]
    public void ChoosingPixelArtPutsItsRulesAndPitfallsIntoThePlanPrompt()
    {
        var user = PlanPromptBuilder.User("Siege", "en", new VideoStyle { Name = "t", DrawStyle = DrawStyle.PixelArt }, Scenes);
        user.ShouldContain("Look: " + DrawStyles.Of(DrawStyle.PixelArt).ImagePrompt);
        user.ShouldContain("Known pitfalls of this look");
        foreach (var pitfall in DrawStyles.Of(DrawStyle.PixelArt).Pitfalls)
        {
            user.ShouldContain(pitfall);
        }

        var look = VideoStylePrompt.Look(new VideoStyle { DrawStyle = DrawStyle.PixelArt });
        look.ShouldContain("Craft: Everything snaps to whole pixels");
        PlanPromptBuilder.User("Siege", "en", new VideoStyle { Name = "t", DrawStyle = DrawStyle.Blueprint }, Scenes).ShouldNotContain("blink");
    }

    [Fact]
    public void TheInputCarriesThePixelLayerOnlyForPixelArtAndNoFinishingLayer()
    {
        var across = Input(1920, 1080);
        RenderStyleBuilder.Apply(across, new VideoStyle { Name = "t", DrawStyle = DrawStyle.PixelArt, Finish = true });
        var layer = across.Style.ShouldNotBeNull().Pixel.ShouldNotBeNull();
        (layer.Width, layer.Height, layer.Steps, layer.Block).ShouldBe((480, 270, 12, PixelArtLimits.BlockSize));
        (1920 / layer.Width).ShouldBe(PixelArtLimits.Scale);
        across.Style.Finish.ShouldBeNull("pixel art has no finishing layer even when the template has it on");
        across.Style.Texture.ShouldBeEmpty();
        layer.Ramps.Keys.ShouldBe(RenderPixelBuilder.Tokens, ignoreOrder: true);
        layer.Ramps.Values.ShouldAllBe(r => r.Count == PixelArtLimits.RampLength);
        layer.Ramps["accent"][2].ShouldBe(across.Style.Colors.Accent.ToUpperInvariant(), "the middle of a ramp is the palette's own colour");
        RenderInputBuilder.ToJson(across).ShouldContain("\"pixel\"");

        var upright = Input(1080, 1920);
        RenderStyleBuilder.Apply(upright, new VideoStyle { Name = "t", DrawStyle = DrawStyle.PixelArt });
        (upright.Style!.Pixel!.Width, upright.Style.Pixel.Height).ShouldBe((270, 480));

        var other = Input(1920, 1080);
        RenderStyleBuilder.Apply(other, new VideoStyle { Name = "t", DrawStyle = DrawStyle.Watercolor, Finish = true });
        other.Style!.Pixel.ShouldBeNull();
        RenderInputBuilder.ToJson(other).ShouldNotContain("\"pixel\"", customMessage: "every other style's input is as before");
        var auto = Input(1920, 1080);
        RenderStyleBuilder.Apply(auto, new VideoStyle { Name = "t", DrawStyle = DrawStyle.Auto });
        auto.Style!.Pixel.ShouldBeNull();
    }

    [Fact]
    public void ARampGoesFromDarkToLightAndIsTheSameEveryTime()
    {
        foreach (var hex in new[] { "#0B1633", "#FFB224", "#FFFFFF", "#000000", "#5BD17A" })
        {
            var ramp = RenderPixelBuilder.Ramp(hex);
            ramp.Count.ShouldBe(PixelArtLimits.RampLength);
            ramp[2].ShouldBe(hex);
            var light = ramp.Select(c => PaletteColors.TryParse(c, out var v) ? PaletteColors.Luminance(v) : -1).ToList();
            for (var i = 1; i < light.Count; i++)
            {
                light[i].ShouldBeGreaterThanOrEqualTo(light[i - 1], hex + " step " + i);
            }

            RenderPixelBuilder.Ramp(hex).ShouldBe(ramp);
        }

        RenderPixelBuilder.Ramp("not a colour")[2].ShouldBe("#808080");
        RenderPixelBuilder.For(new VideoStyle { DrawStyle = DrawStyle.Flat }, new RenderColors(), 1920, 1080).ShouldBeNull();
    }

    [Fact]
    public void TheDrawerHasThePixelLayerTiedToTheTimelineAndItsTransitionsOnlyUncoverTheNewScene()
    {
        var code = PlanDrawer.Code;
        code.ShouldContain("var pixel = style.pixel || null;");
        code.ShouldContain("if (pixel && grounds.length > 0) pixelLayer();");
        code.ShouldContain("if (pixel) pixelGround(scene, plan, camera,");
        code.ShouldContain("image-rendering:pixelated");
        code.ShouldContain("imageSmoothingEnabled = false");
        code.ShouldContain("if (pixel) known = known.concat([\"dissolve\", \"iris\", \"blocks\"]);");

        var layer = Body(code, "function pixelLayer()");
        layer.ShouldContain("tl.time()", Case.Sensitive, "painted from the timeline's time, no other clock");
        code.ShouldNotContain("setInterval");
        code.ShouldNotContain("requestAnimationFrame");
        code.ShouldNotContain("Math.random");

        // A pixel transition clips or masks the new layer and never paints a colour of its own, so it cannot leave black frames.
        var transition = Body(code, "function pixelTransition(");
        transition.ShouldContain("layer.style.clipPath");
        transition.ShouldContain("layer.style.maskImage");
        transition.ShouldNotContain("fillRect");
        transition.ShouldNotContain("background");
        transition.ShouldNotContain("opacity: 0");
        PlanDrawer.IsDrawer(code).ShouldBeTrue();
    }

    [Fact]
    public void APixelDissolveBetweenLitScenesHasNoBlackFramesAThreeFrameBlinkIsReported()
    {
        // The dissolve uncovers the new scene over the old one: every frame lies between the two pictures' brightness.
        var dissolve = Enumerable.Range(0, 30).Select(i => Moving(i, 0.6))
            .Concat(Enumerable.Range(0, 7).Select(s => Mix(Moving(30 + s, 0.6), Moving(30 + s, 1.0), s / 6.0)))
            .Concat(Enumerable.Range(37, 30).Select(i => Moving(i, 1.0)));
        var window = new MotionExpectations([3.0], [], [], [(3.0, 3.6)]);
        Codes(Series(dissolve), window).ShouldNotContain(VideoWarnings.MotionBlack);

        // A transition through black (three frames, as ReelMimic's blink pitfall) is reported even inside the transition's window.
        var blink = Enumerable.Range(0, 30).Select(i => Moving(i, 0.6)).Concat(Enumerable.Repeat(Plain(0), PixelArtLimits.MaxDarkFrames + 1))
            .Concat(Enumerable.Range(33, 30).Select(i => Moving(i, 1.0)));
        Codes(Series(blink), window).ShouldContain(VideoWarnings.MotionBlack);
    }

    private static string Body(string code, string start)
    {
        var at = code.IndexOf(start, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, start);
        var end = code.IndexOf("\n  function ", at + start.Length, StringComparison.Ordinal);
        var next = code.IndexOf("\n  var previous = null;", at, StringComparison.Ordinal);
        return code[at..(end < 0 || (next >= 0 && next < end) ? next : end)];
    }

    private static RenderInput Input(int width, int height) =>
        new() { Title = "Siege", Language = "en", Width = width, Height = height, Fps = 30, DurationSeconds = 6 };

    private const int FW = MotionCheckLimits.FrameWidth;
    private const int FH = MotionCheckLimits.FrameHeight;

    private static byte[] Moving(int k, double light) => Frame((x, y) => (byte)Math.Round((((x + y + (2 * k)) % 200) + 28) * light));

    private static byte[] Plain(byte value) => Frame((_, _) => value);

    private static byte[] Mix(byte[] from, byte[] to, double k) => from.Select((v, i) => (byte)Math.Round(v + ((to[i] - v) * k))).ToArray();

    private static byte[] Frame(Func<int, int, byte> pixel)
    {
        var frame = new byte[FW * FH];
        for (var y = 0; y < FH; y++)
        {
            for (var x = 0; x < FW; x++)
            {
                frame[(y * FW) + x] = pixel(x, y);
            }
        }

        return frame;
    }

    private static MotionSeries Series(IEnumerable<byte[]> frames)
    {
        var reader = new MotionFrameReader(10);
        foreach (var frame in frames)
        {
            reader.Add(frame);
        }

        return reader.Series();
    }

    private static IReadOnlyList<string> Codes(MotionSeries series, MotionExpectations expect) =>
        MotionCheck.Find(series, expect).Select(f => f.Code).ToList();
}
