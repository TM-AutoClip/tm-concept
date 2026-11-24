using Shouldly;
using TMConcept.Core.Plans;
using TMConcept.Core.Render;
using TMConcept.Core.Scenes;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Engines.Media;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.41 (F3): the motion check of a finished video on frame sequences made in memory (no ffmpeg): the frame numbers, freeze, black frames,
/// a white flash, a cut inside a scene, a hard cut to black at the end, a planned fade that did not happen, what the plan expects, and the
/// ffmpeg arguments.
/// </summary>
public sealed class MotionCheckTests
{
    private const int W = MotionCheckLimits.FrameWidth;
    private const int H = MotionCheckLimits.FrameHeight;
    private const double Fps = 10;

    /// <summary>A textured frame that drifts a little every frame (moving, mid-grey, no cut).</summary>
    private static byte[] Moving(int k, double light = 1) => Frame((x, y) => (byte)Math.Round((((x + y + (2 * k)) % 200) + 28) * light));

    /// <summary>The same texture turned over (a different picture: most cells change a lot).</summary>
    private static byte[] Other(int k) => Frame((x, y) => (byte)(255 - ((x + y + (2 * k)) % 200) - 28));

    private static byte[] Plain(byte value) => Frame((_, _) => value);

    private static byte[] Frame(Func<int, int, byte> pixel)
    {
        var frame = new byte[W * H];
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                frame[(y * W) + x] = pixel(x, y);
            }
        }

        return frame;
    }

    private static MotionSeries Series(IEnumerable<byte[]> frames)
    {
        var reader = new MotionFrameReader(Fps);
        foreach (var frame in frames)
        {
            reader.Add(frame);
        }

        return reader.Series();
    }

    private static IEnumerable<byte[]> Moves(int from, int count, double light = 1) => Enumerable.Range(from, count).Select(k => Moving(k, light));

    private static IReadOnlyList<string> Codes(MotionSeries series, MotionExpectations? expect = null) =>
        MotionCheck.Find(series, expect ?? MotionExpectations.None).Select(f => f.Code).ToList();

    [Fact]
    public async Task TheReaderMeasuresBrightnessChangeAndTheChangedCellsFrameByFrame()
    {
        var series = Series([Plain(0), Plain(0), Plain(255), Moving(0)]);
        series.Count.ShouldBe(4);
        series.Luminance[0].ShouldBe(0);
        series.Luminance[2].ShouldBe(1);
        series.Change[0].ShouldBe(0);
        series.Change[1].ShouldBe(0, "the same frame again");
        series.Change[2].ShouldBe(1);
        series.CutShare[2].ShouldBe(1, "every cell changed a lot");
        series.Time(3).ShouldBe(0.3, 1e-9);

        // From a stream: a last part shorter than a frame is left out.
        var reader = new MotionFrameReader(Fps);
        using var stream = new MemoryStream([.. Plain(10), .. Plain(20), 1, 2, 3]);
        await reader.AddAllAsync(stream, CancellationToken.None);
        reader.Series().Count.ShouldBe(2);
        Should.Throw<ArgumentException>(() => new MotionFrameReader(Fps).Add(new byte[10]));
    }

    [Fact]
    public void AMovingVideoHasNoFindings()
    {
        Codes(Series(Moves(0, 40))).ShouldBeEmpty();
    }

    [Fact]
    public void AFreezeIsFoundUnlessThePlanMeansTheStill()
    {
        var still = Moving(10);
        var series = Series([.. Moves(0, 10), .. Enumerable.Repeat(still, 20), .. Moves(11, 10)]);
        var finding = MotionCheck.Find(series, MotionExpectations.None).ShouldHaveSingleItem();
        finding.Code.ShouldBe(VideoWarnings.MotionFreeze);
        finding.From.ShouldBe(1.0, 0.11);
        finding.To.ShouldBe(2.9, 0.11);
        finding.StripSeconds.ShouldBe(MotionCheckLimits.WideStripSeconds);

        Codes(series, new MotionExpectations([], [], [(0.5, 3.5)])).ShouldBeEmpty("a title card or a drawn scene between its beats stands still on purpose");
        Codes(series, new MotionExpectations([], [], [(0.5, 2.0)])).ShouldBe([VideoWarnings.MotionFreeze], "a second of it falls where motion was planned");
        Codes(Series([.. Moves(0, 10), .. Enumerable.Repeat(still, 8), .. Moves(11, 10)])).ShouldBeEmpty("shorter than a second");
    }

    [Fact]
    public void BlackFramesInsideTheFilmAreFoundButNotInAPlannedFadeOrAtTheVeryStart()
    {
        var series = Series([.. Moves(0, 10), Plain(2), Plain(2), Plain(2), .. Moves(13, 10)]);
        var findings = MotionCheck.Find(series, MotionExpectations.None);
        findings.Single(f => f.Code == VideoWarnings.MotionBlack).From.ShouldBe(1.0, 1e-9);
        Codes(series, new MotionExpectations([], [1.1], [])).ShouldBeEmpty("the fade's own dark frames, and its cuts, belong to it");
        Codes(Series([Plain(2), .. Moves(1, 20)])).ShouldNotContain(VideoWarnings.MotionBlack, "the first frame of a video may be black");
    }

    [Fact]
    public void AShortWhiteFlashIsFoundAWhiteCardIsNot()
    {
        Codes(Series([.. Moves(0, 10), Plain(250), Plain(250), .. Moves(12, 10)])).ShouldContain(VideoWarnings.MotionFlash);
        Codes(Series([.. Moves(0, 10), .. Enumerable.Range(0, 20).Select(k => Frame((x, _) => (byte)(240 + ((x + k) % 10)))), .. Moves(30, 10)]))
            .ShouldNotContain(VideoWarnings.MotionFlash, "two seconds of white is a white card");
    }

    [Fact]
    public void ACutInsideASceneIsFoundACutAtASceneStartIsNot()
    {
        var series = Series([.. Moves(0, 10), .. Enumerable.Range(10, 10).Select(Other)]);
        var cut = MotionCheck.Find(series, MotionExpectations.None).ShouldHaveSingleItem();
        cut.Code.ShouldBe(VideoWarnings.MotionCut);
        cut.From.ShouldBe(1.0, 1e-9);
        cut.StripName.ShouldBe("cut-1.00s.png");
        cut.StripSeconds.ShouldBe(0, "a cut is shown frame by frame");
        Codes(series, new MotionExpectations([1.0], [], [])).ShouldBeEmpty();
    }

    [Fact]
    public void TheDrawersTransitionIsNoCutFindingAPlaceholderSceneKeepsTheOldWindow()
    {
        var series = Series([.. Moves(0, 10), .. Enumerable.Range(10, 10).Select(Other)]);
        Codes(series, new MotionExpectations([0.6], [], [])).ShouldBe([VideoWarnings.MotionCut], "0.4 s after the start is no longer the scene's own cut");
        Codes(series, new MotionExpectations([0.6], [], [], [(0.15, 1.05)])).ShouldBeEmpty("a push of 0.45 s moves most of the frame at once");

        var input = new RenderInput
        {
            DurationSeconds = 12,
            Scenes =
            [
                new RenderScene { Number = 1, StartSeconds = 0, DurationSeconds = 4, Plan = new RenderPlan { Base = PlanBases.Title } },
                new RenderScene
                {
                    Number = 2, StartSeconds = 4, DurationSeconds = 4,
                    Plan = new RenderPlan { Base = PlanBases.Title, Transition = "push-left", TransitionSeconds = 0.45 },
                },
                new RenderScene
                {
                    Number = 3, StartSeconds = 8, DurationSeconds = 4, Transition = SceneTransitions.Cut,
                    Plan = new RenderPlan { Base = PlanBases.Title, Transition = "push-left", TransitionSeconds = 0.45 },
                },
            ],
        };
        var window = MotionCheck.Expect(input).Transitions.ShouldNotBeNull().ShouldHaveSingleItem("the placeholder's hard cut wins over the plan");
        window.From.ShouldBe(3.55, 1e-9);
        window.To.ShouldBe(4.45, 1e-9);
    }

    [Fact]
    public void AHardCutToBlackAtTheEndIsFoundAFadeOutIsNot()
    {
        Codes(Series([.. Moves(0, 20), Plain(1), Plain(1)])).ShouldBe([VideoWarnings.MotionEndBlack]);
        var fadeOut = Enumerable.Range(0, 12).Select(i => Moving(20 + i, 1 - (i / 11.0)));
        Codes(Series([.. Moves(0, 20), .. fadeOut])).ShouldBeEmpty();
    }

    [Fact]
    public void APlannedFadeThatDidNotHappenIsFound()
    {
        var fades = new MotionExpectations([2.0], [2.0], []);
        Codes(Series(Moves(0, 40)), fades).ShouldBe([VideoWarnings.MotionFade]);

        // Through black over a quarter second on each side of the scene start: it happened.
        var through = Enumerable.Range(0, 40).Select(i => Moving(i, Math.Min(1, Math.Abs(i - 20) / 3.0)));
        Codes(Series(through), fades).ShouldBeEmpty();

        // Too dark around it to tell: not checked.
        Codes(Series(Moves(0, 40, 0.05)), fades).ShouldNotContain(VideoWarnings.MotionFade);
    }

    [Fact]
    public void ThePlanTellsWhereCutsFadesAndStillsBelong()
    {
        var input = new RenderInput
        {
            DurationSeconds = 12,
            Style = new RenderStyle { EndCardSeconds = 2 },
            Scenes =
            [
                new RenderScene
                {
                    Number = 1, StartSeconds = 0, DurationSeconds = 5,
                    Plan = new RenderPlan
                    {
                        Base = PlanBases.Map,
                        Beats = [new PlanBeat { Start = 2, Actions = [new PlanAction { Type = PlanActions.Pin, At = "a" }] }, new PlanBeat { Start = 4 }],
                    },
                },
                new RenderScene { Number = 2, StartSeconds = 5, DurationSeconds = 7, Transition = SceneTransitions.Fade },
            ],
        };
        var expect = MotionCheck.Expect(input);
        expect.SceneStarts.ShouldBe([5, 10], "the scene start and the closing card's start");
        expect.Fades.ShouldBe([5]);
        expect.Still.ShouldBe([(10, 12), (0, 2), (3, 5)], "a drawn scene stands still outside its moving beats' first second; a beat with nothing does not move");
    }

    [Fact]
    public void EveryFindingIsAYellowWarningWithItsTextsInBothLanguages()
    {
        VideoWarnings.Motion.Count.ShouldBe(6);
        foreach (var language in Localizer.Supported)
        {
            var keys = Localizer.Keys(language).ToHashSet(StringComparer.Ordinal);
            foreach (var code in VideoWarnings.Motion)
            {
                // The badge and the note of the Produced videos page, the video page's activity line and notification, a failed run's note.
                keys.ShouldContain("produced.badge." + code, language);
                keys.ShouldContain("produced.note." + code, language);
                keys.ShouldContain("video." + code, language);
                keys.ShouldContain("video.noted." + code, language);
            }
        }
    }

    [Fact]
    public void TheFfmpegArgumentsReadSmallGreyFramesAndTileAStrip()
    {
        var frames = FfmpegMotionFrames.FrameArguments("v.mp4", 30, "out.gray");
        frames.ShouldContain("fps=30,scale=160:96,format=gray");
        frames.ShouldContain("rawvideo");
        var wide = FfmpegMotionFrames.StripArguments("v.mp4", 10, 2, 30, "s.png");
        wide[wide.ToList().IndexOf("-ss") + 1].ShouldBe("9");
        wide.ShouldContain("fps=5,scale=320:-2,tile=10x1");
        var close = FfmpegMotionFrames.StripArguments("v.mp4", 10, 0, 30, "s.png");
        close[close.ToList().IndexOf("-ss") + 1].ShouldBe("9.833");
        close.ShouldContain("scale=320:-2,tile=10x1");
        close.ShouldContain("-frames:v");
    }
}
