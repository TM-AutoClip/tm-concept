using System.Diagnostics;
using System.Globalization;
using Shouldly;
using TMConcept.Core.Media;
using TMConcept.Core.Profiles;
using TMConcept.Core.Styles;
using TMConcept.Engines.Media;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The mux of a template video (0.1.19): the overlay is burned in with the template's fonts, the channel photo is cut to a circle and laid over
/// the closing card from its start only, and the fades still darken everything.
/// </summary>
public sealed class StyleMuxTests
{
    [Fact]
    public void TheOverlayIsBurnedInWithItsFontFolderAndThePhotoIsLaidOverTheCardFromItsStart()
    {
        var overlay = new MuxOverlay("subtitles.ass", "fonts", new MuxPhoto("channel-photo.png", 378, 600, 324, 16.5, 20, 0.4, 30));

        var arguments = FfmpegMuxer.Arguments("silent.mp4", [("1.wav", 0)], overlay, [4200], "video.mp4");

        var list = arguments.ToList();
        list.IndexOf("-loop").ShouldBeGreaterThan(list.IndexOf("1.wav"), "the photo is the input after the narration clips");
        list[list.IndexOf("-loop") + 1].ShouldBe("1");
        list[list.IndexOf("-t") + 1].ShouldBe("3.5", "looped only for the card's seconds");
        var filter = list[list.IndexOf("-filter_complex") + 1];
        filter.ShouldContain("[2:v]scale=324:324:force_original_aspect_ratio=increase,crop=324:324,format=rgba,geq=");
        filter.ShouldContain("a='if(lte(hypot(X-W/2,Y-H/2),W/2),255,0)'");
        filter.ShouldContain("fade=t=in:st=0:d=0.4:alpha=1,setpts=PTS+16.5/TB[photo]");
        filter.ShouldContain("[0:v]ass=subtitles.ass:fontsdir=fonts[base];[base][photo]overlay=x=378:y=600:eof_action=pass:enable='gte(t,16.5)',fade=t=out:st=3.95");
        filter.ShouldContain("[1:a]aresample=48000");
        list[list.IndexOf("-map") + 1].ShouldBe("[v]");
        arguments.ShouldContain("libx264");
    }

    [Fact]
    public void AnOverlayWithoutPhotoIsOnlyBurnedInAndNothingIsOnlyCopied()
    {
        var burned = FfmpegMuxer.Arguments("silent.mp4", [], new MuxOverlay("subtitles.ass", "fonts"), [], "video.mp4");
        burned[burned.ToList().IndexOf("-filter_complex") + 1].ShouldBe("[0:v]ass=subtitles.ass:fontsdir=fonts[v]");
        burned.ShouldNotContain("-loop");

        var copied = FfmpegMuxer.Arguments("silent.mp4", [], MuxOverlay.None, [], "video.mp4");
        copied.ShouldNotContain("-filter_complex");
        copied.ShouldContain("copy");
    }

    /// <summary>
    /// A real ffmpeg (PATH or TMCONCEPT_TEST_FFMPEG; skipped without one): the progress bar grows from the left, the red photo appears inside its
    /// circle only on the card, not before it and not in the circle's corners.
    /// </summary>
    [Fact]
    public async Task ARealFfmpegDrawsTheBarAndTheRoundPhotoOnTheCard()
    {
        var ffmpeg = PlaybackTests.Ffmpeg();
        if (ffmpeg is null)
        {
            Assert.Skip("ffmpeg was not found (PATH or TMCONCEPT_TEST_FFMPEG).");
        }

        var folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "style-mux-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            const int width = 360;
            const int height = 640;
            const double seconds = 5;
            await RunAsync(ffmpeg, folder, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
                "color=c=0x00FF00:size=360x640:rate=30:duration=5", "-c:v", "libx264", "-pix_fmt", "yuv420p", "silent.mp4"]);
            await RunAsync(ffmpeg, folder, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=red:size=120x90", "-frames:v", "1",
                "channel-photo.png"]);
            var style = new VideoStyle { Name = "t", Subtitles = false, ProgressBar = true, Closing = ClosingPlacement.Screen };
            var closing = VideoStyleRules.Closing(style, ContentLanguages.English);
            var drawn = StyleOverlay.Build(width, height, seconds, [], style, ContentLanguages.English, closing, "Channel", photo: true);
            await File.WriteAllTextAsync(Path.Combine(folder, "subtitles.ass"), drawn.Ass!, TestContext.Current.CancellationToken);
            VideoFontFiles.CopyTo(VideoStyleRules.Font(style, ContentLanguages.English), Path.Combine(folder, "fonts"));
            var card = drawn.Card!;
            var overlay = new MuxOverlay("subtitles.ass", "fonts",
                new MuxPhoto("channel-photo.png", card.PhotoX, card.PhotoY, card.PhotoSize, card.StartSeconds, card.EndSeconds, 0.2, 30));

            await RunAsync(ffmpeg, folder, [.. FfmpegMuxer.Arguments("silent.mp4", [], overlay, [], "video.mp4")]);

            var centreX = card.PhotoX + (card.PhotoSize / 2);
            var centreY = card.PhotoY + (card.PhotoSize / 2);
            var bar = (int)Math.Round(height * StyleLayout.ProgressBarHeight) / 2;
            var early = await PixelAsync(ffmpeg, folder, 1.0, 20, bar);
            early.R.ShouldBeGreaterThan(200, "the bar has passed this point at 1 s: amber accent");
            early.B.ShouldBeLessThan(120);
            var ahead = await PixelAsync(ffmpeg, folder, 1.0, 330, bar);
            ahead.G.ShouldBeGreaterThan(ahead.R, "not reached yet at 1 s: the green picture under a dim track");
            var before = await PixelAsync(ffmpeg, folder, 1.0, centreX, centreY);
            before.G.ShouldBeGreaterThan(200, "no photo before the card");
            var on = await PixelAsync(ffmpeg, folder, 4.8, centreX, centreY);
            on.R.ShouldBeGreaterThan(180, "the photo in the middle of the circle");
            on.G.ShouldBeLessThan(80);
            var corner = await PixelAsync(ffmpeg, folder, 4.8, card.PhotoX + 3, card.PhotoY + 3);
            corner.R.ShouldBeLessThan(150, "outside the circle the photo is cut away");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<(int R, int G, int B)> PixelAsync(string ffmpeg, string folder, double seconds, int x, int y)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"px-{seconds:0.0}-{x}-{y}.rgb");
        await RunAsync(ffmpeg, folder, ["-hide_banner", "-loglevel", "error", "-y", "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", "video.mp4",
            "-frames:v", "1", "-vf", string.Create(CultureInfo.InvariantCulture, $"format=rgb24,crop=1:1:{x}:{y}"), "-f", "rawvideo", "-pix_fmt", "rgb24", name]);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(folder, name), TestContext.Current.CancellationToken);
        return (bytes[0], bytes[1], bytes[2]);
    }

    private static async Task RunAsync(string ffmpeg, string folder, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.ShouldBe(0, error);
    }
}
