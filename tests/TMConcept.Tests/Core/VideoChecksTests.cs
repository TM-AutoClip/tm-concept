using Shouldly;
using TMConcept.Core.Engines;
using TMConcept.Core.Media;
using TMConcept.Core.Render;
using TMConcept.Core.Videos;
using TMConcept.Engines.Engines;

namespace TMConcept.Tests.Core;

/// <summary>
/// The checks of 20 Nov 2026 that keep an empty video from being "ready": the code must show the scene images when there are any
/// (<see cref="ImageUseCheck"/>), the repair example of every engine shows them, and the frame check after the render
/// (<see cref="FrameCheck"/>) fails a near-uniform frame and a frame that does not resemble its image, conservatively.
/// </summary>
public sealed class VideoChecksTests
{
    private static RenderInput Input(bool images) => RenderInputBuilder.Build("T", "en",
    [
        new RenderSceneSource("a map", "One.", images ? "scene-1.png" : string.Empty, 2000),
        new RenderSceneSource("ships", "Two.", images ? "scene-2.png" : string.Empty, 2000),
    ], 540, 960, 30);

    [Theory]
    [InlineData(EngineKind.HyperFrames, "const p = document.createElement(\"img\"); p.src = scene.image;", true)]
    [InlineData(EngineKind.HyperFrames, "box.style.backgroundImage = scene.image;", true)]
    [InlineData(EngineKind.HyperFrames, "const {image} = scene; picture.src = image;", true)]
    [InlineData(EngineKind.HyperFrames, "layer.style.background = \"url(\" + scene.image + \") center/cover\";", true)]
    [InlineData(EngineKind.HyperFrames, "layer.style.cssText = `background:url(${scene.image}) center/cover`;", true)]
    [InlineData(EngineKind.HyperFrames, "const p = document.createElementNS(svg, \"image\"); p.setAttribute(\"href\", scene.image);", true)]
    [InlineData(EngineKind.HyperFrames, "p.setAttributeNS(null, 'href', scene.image);", true)]
    [InlineData(EngineKind.HyperFrames, "box.textContent = scene.visual;", false)]
    [InlineData(EngineKind.HyperFrames, "const name = scene.image; box.textContent = name;", false)]
    [InlineData(EngineKind.Remotion, "<Img src={staticFile(scene.image)} />", true)]
    [InlineData(EngineKind.Remotion, "<div>{scene.visual}</div>", false)]
    [InlineData(EngineKind.Revideo, "view.add(<Img src={'/' + scene.image} />);", true)]
    [InlineData(EngineKind.Revideo, "view.add(<Txt text={scene.image} />);", false)]
    public void TheCodeMustShowTheSceneImagesInTheEnginesOwnWay(EngineKind engine, string code, bool shows) =>
        ImageUseCheck.Shows(engine, code).ShouldBe(shows);

    [Fact]
    public void ThereIsAFindingOnlyWhenTheScenesHaveImages()
    {
        ImageUseCheck.Finding(EngineKind.HyperFrames, "box.textContent = scene.visual;", Input(images: false)).ShouldBeNull();
        var finding = ImageUseCheck.Finding(EngineKind.HyperFrames, "box.textContent = scene.visual;", Input(images: true)).ShouldNotBeNull();
        finding.ShouldStartWith(ImageUseCheck.FindingStart);
        VideoCodePrompt.Fix(finding).ShouldContain("full frame");
    }

    [Fact]
    public void EveryEnginesRepairExampleAndOwnAnimationShowTheImagesAndPassTheChecks()
    {
        var engines = new (EngineKind Kind, string Sample, string Fallback)[]
        {
            (EngineKind.HyperFrames, Sample<HyperFramesEngine>(), Fallback<HyperFramesEngine>()),
            (EngineKind.Remotion, Sample<RemotionEngine>(), Sample<RemotionEngine>()),
            (EngineKind.Revideo, Sample<RevideoEngine>(), Sample<RevideoEngine>()),
        };
        foreach (var (kind, sample, fallback) in engines)
        {
            ImageUseCheck.Finding(kind, sample, Input(images: true)).ShouldBeNull(kind.ToString());
            ImageUseCheck.Finding(kind, fallback, Input(images: true)).ShouldBeNull(kind.ToString());
            EngineCodeCheck.Check(kind, sample).ShouldBeEmpty(kind.ToString());
        }
    }

    private static string Sample<T>()
        where T : IVideoEngine => ((IVideoEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T))).SampleCode;

    private static string Fallback<T>()
        where T : IVideoEngine => ((IVideoEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T))).FallbackCode;

    private static FramePixels Pixels(int width, int height, Func<int, int, byte> grey) =>
        new(width, height, Enumerable.Range(0, width * height).SelectMany(i => Enumerable.Repeat(grey(i % width, i / width), 3)).ToArray());

    /// <summary>A picture with structure: a bright disc on a dark sky and a gradient.</summary>
    private static FramePixels Picture() => Pixels(96, 48, (x, y) => (byte)Math.Clamp(((x - 30) * (x - 30)) + ((y - 20) * (y - 20)) < 120 ? 230 : (x * 2) + y, 0, 255));

    [Fact]
    public void AFlatFrameFailsAndAFrameWithASmallTitleDoesNot()
    {
        // The 19 Nov 2026 video: a dark red background with a thin bar (8 px of 1920 averaged into one of 48 rows).
        var empty = Pixels(96, 48, (x, y) => (byte)(y == 4 && x > 10 && x < 86 ? 46 : 40));
        var score = FrameCheck.Score(1, empty, null);
        score.Failed.ShouldBeTrue($"deviation {score.Deviation}");
        score.Finding.ShouldStartWith("scene 1 shows an empty frame");

        // A plain background with a short title in the middle: a design, not an empty frame.
        var title = Pixels(96, 48, (x, y) => (byte)(y is >= 22 and <= 25 && x is >= 36 and <= 60 ? 240 : 30));
        FrameCheck.Score(1, title, null).Failed.ShouldBeFalse();
    }

    [Fact]
    public void AFrameMustResembleItsSceneImageThroughZoomTintAndABox()
    {
        var image = Picture();
        FrameCheck.Score(1, image, image).Resemblance!.Value.ShouldBe(1, 1e-9);

        // Zoomed in by about 10 %: the centre of the image fills the frame.
        var zoomed = Pixels(96, 48, (x, y) =>
        {
            var source = image.Rgb.Span;
            var sx = (int)(48 + ((x - 48) / 1.1));
            var sy = (int)(24 + ((y - 24) / 1.1));
            return source[((sy * 96) + sx) * 3];
        });
        FrameCheck.Score(1, zoomed, image).Failed.ShouldBeFalse();

        // Darkened and tinted under a few shapes: still the image.
        var darker = Pixels(96, 48, (x, y) => (byte)(image.Rgb.Span[((y * 96) + x) * 3] * 0.6 + (x < 8 ? 120 : 0)));
        FrameCheck.Score(1, darker, image).Failed.ShouldBeFalse();

        // Shown in a box of 60 % in the middle of a plain frame.
        var boxed = Pixels(96, 48, (x, y) =>
        {
            var inside = x >= 19 && x < 77 && y >= 10 && y < 38;
            return inside ? image.Rgb.Span[((((y - 10) * 48 / 28 * 96) + ((x - 19) * 96 / 58)) * 3)] : (byte)20;
        });
        FrameCheck.Score(1, boxed, image).Failed.ShouldBeFalse();

        // Something else entirely: a text card with stripes.
        var other = Pixels(96, 48, (x, y) => (byte)(y % 12 < 6 ? 200 : 60));
        var score = FrameCheck.Score(2, other, image);
        score.Failed.ShouldBeTrue($"resemblance {score.Resemblance}");
        score.Finding.ShouldStartWith("scene 2 does not show its image");
    }

    [Fact]
    public void AnImageInOneHalfOfTheFrameOrMidPanStillResemblesIt()
    {
        var image = Picture();
        byte At(int x, int y) => image.Rgb.Span[((Math.Clamp(y, 0, 47) * 96) + Math.Clamp(x, 0, 95)) * 3];

        // The middle half of the image covering the top half of the frame, a text card with lines below it.
        var top = Pixels(96, 48, (x, y) => y < 24 ? At(x, 12 + y) : (byte)(y % 6 < 2 && x is > 10 and < 86 ? 220 : 25));
        FrameCheck.Score(1, top, image).Failed.ShouldBeFalse($"resemblance {FrameCheck.Resemblance(top, image)}");

        // A left/right split: the image's middle half on the right, a plain panel on the left.
        var split = Pixels(96, 48, (x, y) => x >= 48 ? At(24 + (x - 48), y) : (byte)40);
        FrameCheck.Score(1, split, image).Failed.ShouldBeFalse($"resemblance {FrameCheck.Resemblance(split, image)}");

        // Mid-pan: zoomed in by a quarter and moved to the left edge (a translate, not a centred zoom).
        var pan = Pixels(96, 48, (x, y) => At((int)(x * 0.8), (int)(y * 0.8)));
        FrameCheck.Score(1, pan, image).Failed.ShouldBeFalse($"resemblance {FrameCheck.Resemblance(pan, image)}");
    }

    [Fact]
    public void APlainSceneImageCannotBeToldFromAnEmptyFrameSoItPasses()
    {
        var night = Pixels(96, 48, (_, _) => 12);
        FrameCheck.Score(1, night, night).Failed.ShouldBeFalse();
    }
}
