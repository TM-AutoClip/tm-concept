using System.Diagnostics;
using System.Globalization;
using Shouldly;
using TMConcept.Engines.Media;

namespace TMConcept.Tests.Engines;

public sealed class MuxTests
{
    [Fact]
    public void ClipsArePlacedAtTheirSceneStartsAndSubtitlesAreBurnedIn()
    {
        var arguments = FfmpegMuxer.Arguments("silent.mp4", [("1.wav", 0), ("2.wav", 4200)], "subtitles.ass", [], "video.mp4");

        var filter = arguments[arguments.ToList().IndexOf("-filter_complex") + 1];
        filter.ShouldContain("[0:v]ass=subtitles.ass[v]");
        filter.ShouldContain("[2:a]aresample=48000,adelay=4200|4200[a1]");
        filter.ShouldContain("amix=inputs=2:normalize=0");
        arguments.ShouldContain("libx264");
        arguments[^1].ShouldBe("video.mp4");
    }

    [Fact]
    public void WithoutNarrationSubtitlesAndFadesTheVideoIsOnlyCopied()
    {
        var arguments = FfmpegMuxer.Arguments("silent.mp4", [], null, [], "video.mp4");

        arguments.ShouldNotContain("-filter_complex");
        arguments.ShouldContain("copy");
    }

    [Fact]
    public void ASoftTransitionFadesOutBeforeTheSceneStartAndInAfterItInInvariantSecondsOnlyThere()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            FfmpegMuxer.Fades([4200, 0, 4200]).ShouldBe(
            [
                "fade=t=out:st=3.95:d=0.25:enable='between(t,3.95,4.2)'",
                "fade=t=in:st=4.2:d=0.25:enable='between(t,4.2,4.45)'",
            ], "once per start, never at the very start, a dot for the decimals");
            FfmpegMuxer.Fades([100]).First().ShouldBe("fade=t=out:st=0:d=0.1:enable='between(t,0,0.1)'", "it never starts before the video");

            // With subtitles the fades come after them (the whole picture goes dark); without, the video is encoded for them.
            var burned = FfmpegMuxer.Arguments("silent.mp4", [], "subtitles.ass", [4200], "video.mp4");
            burned[burned.ToList().IndexOf("-filter_complex") + 1].ShouldStartWith("[0:v]ass=subtitles.ass,fade=t=out:st=3.95");
            var faded = FfmpegMuxer.Arguments("silent.mp4", [("1.wav", 0)], null, [4200], "video.mp4");
            var filter = faded[faded.ToList().IndexOf("-filter_complex") + 1];
            filter.ShouldStartWith("[0:v]fade=t=out:st=3.95:d=0.25:enable='between(t,3.95,4.2)',fade=t=in:st=4.2:d=0.25:enable='between(t,4.2,4.45)'[v];");
            faded[faded.ToList().IndexOf("-map") + 1].ShouldBe("[v]");
            faded.ShouldContain("libx264");
            faded.ShouldNotContain("copy");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>A real ffmpeg (PATH or TMCONCEPT_TEST_FFMPEG; skipped without one): a white clip is dark at the fade and white elsewhere.</summary>
    [Fact]
    public async Task ARealFfmpegDarkensOnlyAroundTheSoftTransition()
    {
        var ffmpeg = PlaybackTests.Ffmpeg();
        if (ffmpeg is null)
        {
            Assert.Skip("ffmpeg was not found (PATH or TMCONCEPT_TEST_FFMPEG).");
        }

        var folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "fade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var silent = Path.Combine(folder, "silent.mp4");
            await RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=white:size=64x36:rate=30:duration=2",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", silent]);
            var output = Path.Combine(folder, "video.mp4");
            await RunAsync(ffmpeg, [.. FfmpegMuxer.Arguments(silent, [], null, [1000], output)]);

            (await LumaAsync(ffmpeg, output, 0.5)).ShouldBeGreaterThan(200, "before the fade the picture is unchanged");
            (await LumaAsync(ffmpeg, output, 1.0)).ShouldBeLessThan(40, "dark at the scene's start");
            (await LumaAsync(ffmpeg, output, 1.6)).ShouldBeGreaterThan(200, "after the fade the picture is back (a plain fade would stay black)");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<int> LumaAsync(string ffmpeg, string video, double seconds)
    {
        var frame = Path.Combine(Path.GetDirectoryName(video)!, "frame-" + seconds.ToString("0.0", CultureInfo.InvariantCulture) + ".gray");
        await RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", video,
            "-frames:v", "1", "-vf", "scale=1:1", "-f", "rawvideo", "-pix_fmt", "gray", frame]);
        return (await File.ReadAllBytesAsync(frame, TestContext.Current.CancellationToken))[0];
    }

    private static async Task RunAsync(string ffmpeg, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
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
