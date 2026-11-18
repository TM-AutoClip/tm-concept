using System.Diagnostics;
using Shouldly;
using TMConcept.Core.Media;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Core.Subtitles;
using TMConcept.Engines.Engines;
using TMConcept.Engines.Media;

namespace TMConcept.Tests.Engines;

/// <summary>
/// Readable text where it matters (0.1.25): the background under text is measured on generated pictures (flat, half and half, noise,
/// sparse highlights) and the outline gets stronger, or a subtle backdrop is added, only where the contrast is low or the picture is busy.
/// </summary>
public sealed class TextBackdropTests
{
    private const string White = "#FFFFFF";

    private static FramePixels Picture(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                var at = ((y * width) + x) * 3;
                (rgb[at], rgb[at + 1], rgb[at + 2]) = (r, g, b);
            }
        }

        return new FramePixels(width, height, rgb);
    }

    private static FramePixels Flat(byte r, byte g, byte b) => Picture(96, 48, (_, _) => (r, g, b));

    private static FramePixels Noise(int seed)
    {
        var random = new Random(seed);
        return Picture(96, 48, (_, _) =>
        {
            var v = (byte)random.Next(256);
            return (v, v, v);
        });
    }

    private static TextBackdropNeed NeedOf(FramePixels pixels, string text) => TextBackdrop.Need(TextBackdrop.Measure(pixels, text));

    [Fact]
    public void WhiteTextOverACalmDarkPictureNeedsNothingMore()
    {
        var measure = TextBackdrop.Measure(Flat(10, 20, 40), White);

        measure.Contrast.ShouldBeGreaterThan(15);
        (measure.LowContrastShare, measure.Busyness).ShouldBe((0, 0));
        TextBackdrop.Need(measure).ShouldBe(TextBackdropNeed.Plain);
        NeedOf(Flat(255, 255, 255), "#1F1F1F").ShouldBe(TextBackdropNeed.Plain, "dark text over white is fine too");
    }

    [Fact]
    public void ALowContrastOrBusyPictureGetsAStrongerOutlineOrABackdrop()
    {
        // A sky as bright as the text: everything is close to it.
        NeedOf(Flat(245, 248, 255), White).ShouldBe(TextBackdropNeed.Backdrop);

        // Mid grey: no pixel is close to white (4.48 : 1) but the mean is under WCAG's 4.5.
        var grey = TextBackdrop.Measure(Flat(0x77, 0x77, 0x77), White);
        grey.LowContrastShare.ShouldBe(0);
        grey.Contrast.ShouldBeLessThan(4.5);
        TextBackdrop.Need(grey).ShouldBe(TextBackdropNeed.Stronger);

        // Half sky, half sea: half the picture is close to white.
        NeedOf(Picture(96, 48, (x, _) => x < 48 ? ((byte)250, (byte)250, (byte)250) : ((byte)5, (byte)20, (byte)60)), White)
            .ShouldBe(TextBackdropNeed.Backdrop);

        // Dark with a few bright windows (6 % of it, but they make it busy): stronger.
        NeedOf(Picture(96, 48, (x, y) => x % 8 == 0 && y % 2 == 0 ? ((byte)255, (byte)255, (byte)230) : ((byte)15, (byte)15, (byte)25)), White)
            .ShouldBe(TextBackdropNeed.Stronger);

        // Noise is busy whatever its mean.
        var noise = TextBackdrop.Measure(Noise(7), White);
        noise.Busyness.ShouldBeGreaterThan(0.14);
        TextBackdrop.Need(noise).ShouldBe(TextBackdropNeed.Backdrop);
    }

    [Fact]
    public void TheMeasureRefusesPixelsThatDoNotMatchTheirSize() =>
        Should.Throw<ArgumentException>(() => TextBackdrop.Measure(new FramePixels(10, 10, new byte[30]), White));

    [Fact]
    public void ManySubtitlesAreSampledEvenlyAndTheOthersTakeTheNearestNeed()
    {
        TextBackdrop.SampleIndexes(5).ShouldBe([0, 1, 2, 3, 4]);
        var many = TextBackdrop.SampleIndexes(100);
        many.Count.ShouldBe(TextBackdrop.MaxSamples);
        (many[0], many[^1]).ShouldBe((0, 99));

        TextBackdrop.Spread(5, new Dictionary<int, TextBackdropNeed> { [0] = TextBackdropNeed.Plain, [4] = TextBackdropNeed.Backdrop })
            .ShouldBe([TextBackdropNeed.Plain, TextBackdropNeed.Plain, TextBackdropNeed.Plain, TextBackdropNeed.Backdrop, TextBackdropNeed.Backdrop]);
        TextBackdrop.Spread(2, new Dictionary<int, TextBackdropNeed>()).ShouldBe([TextBackdropNeed.Plain, TextBackdropNeed.Plain], "nothing could be read");
    }

    [Fact]
    public void TheSubtitleRegionIsTheBottomBandBetweenTheSideMargins()
    {
        var region = TextBackdrop.SubtitleRegion(1080, 1920);

        region.X.ShouldBe(0.05, 0.001);
        region.Width.ShouldBe(0.9, 0.001);
        (region.Y + region.Height).ShouldBeLessThanOrEqualTo(1);
        region.Y.ShouldBeGreaterThan(0.7, "near the bottom");
        region.Height.ShouldBeGreaterThan(0.05);
    }

    [Fact]
    public void ASubtitleGetsAStrongerEdgeOrABoxOnlyWhereItNeedsOne()
    {
        IReadOnlyList<SubtitleCue> cues = [new(0, 1000, ["Over the night sea."]), new(1000, 2000, ["Over a grey wall."]), new(2000, 3000, ["Over the sky."])];
        TextBackdropNeed[] needs = [TextBackdropNeed.Plain, TextBackdropNeed.Stronger, TextBackdropNeed.Backdrop];
        var style = new VideoStyle { Name = "t", Subtitles = true, PaletteId = "ocean" };

        var ass = StyleOverlay.Build(1080, 1920, 10, cues, style, ContentLanguages.English, null, "c", false, needs).Ass!;

        // Ocean's text is near white: black edge 7, shadow 3; the box is black, 35 % see-through, style 3 (opaque box).
        ass.ShouldContain("Style: TmBoxed,Fira Sans,59,&H00FFFBF1,&H000000FF,&H59000000,&H59000000,1,0,0,0,100,100,0,0,3,11,0,2,");
        ass.ShouldContain(",Default,,0,0,0,,Over the night sea.");
        ass.ShouldContain(@",Default,,0,0,0,,{\bord11\shad5\4a&H33&}Over a grey wall.");
        ass.ShouldContain(",TmBoxed,,0,0,0,,Over the sky.");

        // Readable text off: the measure is not used, the look of 0.1.19 stays.
        var off = StyleOverlay.Build(1080, 1920, 10, cues, new VideoStyle { Name = "t", Subtitles = true, PaletteId = "ocean", ReadableText = false },
            ContentLanguages.English, null, "c", false, needs).Ass!;
        off.ShouldNotContain(",TmBoxed,,");
        off.ShouldNotContain(@"\bord");
    }

    [Fact]
    public void TheEnginesWidenTheOutlineOverHardImagesAndAddAHaloForABackdrop()
    {
        HyperFramesEngine.Readable("#000000").ShouldBe(
            "#root { --tm-outline: #000000; text-shadow: 0 0 0.05em #000000, 0 0 0.05em #000000, 0 0 0.1em #000000, 0 0.04em 0.14em #00000099; }\n",
            "boost 1 is the look of 0.1.24");
        HyperFramesEngine.Readable("#000000", TextBackdrop.StrongerBoost).ShouldContain("0 0 0.08em #000000, 0 0 0.08em #000000, 0 0 0.16em #000000");
        HyperFramesEngine.Readable("#000000", TextBackdrop.StrongerBoost).ShouldNotContain("0.5em");
        HyperFramesEngine.Readable("#FFFFFF", TextBackdrop.BackdropBoost).ShouldContain(", 0 0 0.5em #FFFFFF80; }");

        var input = SampleRender.Input();
        RenderStyleBuilder.Apply(input, new VideoStyle { Name = "t", PaletteId = "forest" });
        input.Style!.OutlineBoost.ShouldBe(1, "until the images are measured");
        input.Style.OutlineBoost = TextBackdrop.BackdropBoost;
        HyperFramesEngine.IndexHtml(input).ShouldContain("0 0 0.5em #00000080");
        RenderInputBuilder.ToJson(input).ShouldContain("\"outlineBoost\": 2.2");

        RemotionEngine.InputTs.ShouldContain("outlineBoost?: number;");
        TemplateHelpers.RevideoHelpers.ShouldContain("input.style?.outlineBoost ?? 1");
        TemplateHelpers.RevideoHelpers.ShouldContain("shadowBlur");
    }

    [Fact]
    public void AnFfmpegSampleCutsTheRegionAndAveragesItDown()
    {
        var arguments = FfmpegMedia.SampleArguments("silent.mp4", 2500, new FrameRegion(0.05, 0.8, 0.9, 0.1), ".sample.rgb");

        arguments.ShouldContain("2.5");
        arguments[arguments.ToList().IndexOf("-ss") + 2].ShouldBe("-i", "fast seek before the input");
        arguments.ShouldContain("crop=w=iw*0.9:h=ih*0.1:x=iw*0.05:y=ih*0.8,scale=96:48:flags=area,format=rgb24");
        arguments[^1].ShouldBe(".sample.rgb");
        FfmpegMedia.SampleArguments("a.png", 0, new FrameRegion(0.5, 0.95, 0.9, 0.2), "o").ShouldContain(
            "crop=w=iw*0.9:h=ih*0.2:x=iw*0.1:y=ih*0.8,scale=96:48:flags=area,format=rgb24", customMessage: "a region past the edge is moved inside");
    }

    /// <summary>A real ffmpeg (PATH or TMCONCEPT_TEST_FFMPEG; skipped without one): generated pictures are sampled and measured.</summary>
    [Fact]
    public async Task ARealFfmpegSamplesGeneratedPictures()
    {
        var ffmpeg = PlaybackTests.Ffmpeg();
        if (ffmpeg is null)
        {
            Assert.Skip("ffmpeg was not found (PATH or TMCONCEPT_TEST_FFMPEG).");
        }

        var folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "backdrop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var sky = Path.Combine(folder, "sky.png");
            var noise = Path.Combine(folder, "noise.png");
            var night = Path.Combine(folder, "night.png");
            await RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=0xF5F8FF:size=320x180", "-frames:v", "1", sky]);
            await RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=gray:size=320x180", "-vf", "noise=alls=100:allf=u",
                "-frames:v", "1", noise]);
            await RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=0x0A1428:size=320x180", "-frames:v", "1", night]);

            async Task<TextBackdropNeed> Need(string file)
            {
                var output = Path.Combine(folder, Path.GetFileNameWithoutExtension(file) + ".rgb");
                await RunAsync(ffmpeg, [.. FfmpegMedia.SampleArguments(file, 0, FrameRegion.Whole, output)]);
                var bytes = await File.ReadAllBytesAsync(output);
                bytes.Length.ShouldBe(FfmpegMedia.SampleWidth * FfmpegMedia.SampleHeight * 3);
                return NeedOf(new FramePixels(FfmpegMedia.SampleWidth, FfmpegMedia.SampleHeight, bytes), White);
            }

            (await Need(sky)).ShouldBe(TextBackdropNeed.Backdrop);
            (await Need(noise)).ShouldNotBe(TextBackdropNeed.Plain);
            (await Need(night)).ShouldBe(TextBackdropNeed.Plain);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task RunAsync(string ffmpeg, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, error);
    }
}
