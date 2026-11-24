using System.Diagnostics;
using Shouldly;
using TMConcept.Core.References;
using TMConcept.Core.Tools;
using TMConcept.Engines.Media;
using TMConcept.Engines.References;
using TMConcept.Engines.Tools;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The engine side of the reference video (S1, 0.1.52): yt-dlp's command line (one video, no playlist, no cookies or sign-in, the user's
/// configuration ignored, at most 480p, the first 10 minutes, at most 400 MB, the address after "--"), its progress lines and the file it
/// leaves, the pinned yt-dlp rows, ffmpeg's frame arguments, and — with a real ffmpeg only (PATH or TMCONCEPT_TEST_FFMPEG; skipped without
/// one) — a test pattern made on this computer measured end to end. yt-dlp is never run; nothing reaches the network.
/// </summary>
public sealed class ReferenceVideoEngineTests
{
    [Fact]
    public void YtDlpDownloadsOneVideoWithoutCookiesAtALowResolution()
    {
        var folder = Path.Combine("work", "reference", "abc", "download");
        var arguments = YtDlpDownloader.Arguments(new Uri("https://example.com/watch?v=1&list=PL2"), folder, "ffmpeg.exe", string.Empty);

        arguments.ShouldContain("--ignore-config");
        arguments.ShouldContain("--no-plugin-dirs");
        arguments.ShouldContain("--no-playlist");
        arguments.ShouldContain("--no-cookies");
        arguments.ShouldContain("--no-cookies-from-browser");
        arguments.ShouldNotContain("--cookies");
        arguments.ShouldNotContain("--username");
        arguments.ShouldNotContain("--netrc");
        arguments.ShouldNotContain("--js-runtimes", "no runtime without the app's Node.js");
        Value(arguments, "-f").ShouldBe("bv*[height<=480]+ba/b[height<=480]/wv*+ba/w");
        Value(arguments, "-S").ShouldBe("res:360");
        Value(arguments, "--download-sections").ShouldBe("*0-600");
        Value(arguments, "--max-filesize").ShouldBe("400M");
        Value(arguments, "--ffmpeg-location").ShouldBe("ffmpeg.exe");
        Value(arguments, "-o").ShouldBe(Path.Combine(folder, "reference.%(ext)s"));
        arguments[^2].ShouldBe("--", "the address can never be read as an option");
        arguments[^1].ShouldBe("https://example.com/watch?v=1&list=PL2");
        arguments.Count(a => a.StartsWith("http", StringComparison.Ordinal)).ShouldBe(1);

        YtDlpDownloader.Arguments(new Uri("https://example.com/v"), folder, "ffmpeg.exe", "node.exe").ShouldContain("node:node.exe");
    }

    [Fact]
    public void YtDlpsProgressAndItsFileAreRead()
    {
        YtDlpDownloader.Progress("[download]  42.3% of ~ 12.34MiB at  1.2MiB/s ETA 00:05").ShouldNotBeNull().ShouldBe(0.423, 1e-9);
        YtDlpDownloader.Progress("[download] 100% of 3.00MiB in 00:00:02").ShouldNotBeNull().ShouldBe(1);
        YtDlpDownloader.Progress("[youtube] abc: Downloading webpage").ShouldBeNull();
        YtDlpDownloader.Progress(string.Empty).ShouldBeNull();

        var folder = Path.Combine(TestFolders.Root, "ytdlp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            YtDlpDownloader.Downloaded(folder).ShouldBeNull();
            File.WriteAllText(Path.Combine(folder, "reference.mkv.part"), new string('x', 500));
            File.WriteAllText(Path.Combine(folder, "reference.f137.mp4"), "xx");
            File.WriteAllText(Path.Combine(folder, "reference.mkv"), "xxxx");
            YtDlpDownloader.Downloaded(folder).ShouldBe(Path.Combine(folder, "reference.mkv"), "the largest finished file, never a part");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void YtDlpIsPinnedForEverySystemAndNeverTakenFromThePath()
    {
        var rows = ToolManifest.Archives.Where(a => a.Tool == ToolKind.VideoDownloader).ToList();
        rows.Select(r => r.Rid).Order().ShouldBe(["linux-arm64", "linux-x64", "win-x64"]);
        rows.ShouldAllBe(r => r.Version == ToolManifest.YtDlpVersion && r.Sha256.Length == 64 && r.Url.Host == "github.com"
            && r.Url.AbsolutePath.StartsWith("/yt-dlp/yt-dlp/releases/download/", StringComparison.Ordinal) && ToolInstaller.IsProgram(r));
        ToolManifest.Libraries.ShouldContain(ToolKind.VideoDownloader);
    }

    [Fact]
    public void FfmpegReadsOnlyTheMeasuredPartAsSmallFrames()
    {
        var grey = FfmpegReferenceVideo.FrameArguments("in.mp4", "out.gray");
        Value(grey, "-t").ShouldBe("600");
        Value(grey, "-vf").ShouldBe("fps=10,scale=160:96,format=gray");
        grey.ShouldContain("-an");
        grey[^1].ShouldBe("out.gray");
        Value(FfmpegReferenceVideo.ColorArguments("in.mp4", "out.rgb"), "-vf").ShouldBe("fps=1,scale=64:36,format=rgb24");
    }

    /// <summary>
    /// A real ffmpeg (PATH or TMCONCEPT_TEST_FFMPEG; skipped without one): a moving test pattern cut to a red picture every 2 seconds, made on
    /// this computer, read with the app's frame arguments: the cuts are found and the red is among the main colours.
    /// </summary>
    [Fact]
    public async Task ARealFfmpegTestPatternIsMeasured()
    {
        var ffmpeg = PlaybackTests.Ffmpeg();
        if (ffmpeg is null)
        {
            Assert.Skip("ffmpeg was not found (PATH or TMCONCEPT_TEST_FFMPEG).");
        }

        var folder = Path.Combine(TestFolders.Root, "reference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var video = Path.Combine(folder, "pattern.mp4");
            await RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30:duration=2", "-f", "lavfi",
                "-i", "color=c=red:size=320x180:rate=30:duration=2", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30:duration=2", "-filter_complex",
                "[0:v][1:v][2:v]concat=n=3:v=1[v]", "-map", "[v]", "-c:v", "libx264", "-pix_fmt", "yuv420p", video]);
            var gray = Path.Combine(folder, "frames.gray");
            await RunAsync(ffmpeg, FfmpegReferenceVideo.FrameArguments(video, gray));
            var frames = new ReferenceFrameReader(ReferenceLimits.FrameFps);
            await using (var stream = File.OpenRead(gray))
            {
                await frames.AddAllAsync(stream, TestContext.Current.CancellationToken);
            }

            var rgb = Path.Combine(folder, "frames.rgb");
            await RunAsync(ffmpeg, FfmpegReferenceVideo.ColorArguments(video, rgb));
            var colors = new ReferenceColorReader();
            await using (var stream = File.OpenRead(rgb))
            {
                await colors.AddAllAsync(stream, TestContext.Current.CancellationToken);
            }

            var series = frames.Series();
            series.Seconds.ShouldBe(6, 0.2);
            ReferenceCuts.Find(series).Count.ShouldBe(2);
            colors.Result().ShouldNotBeNull().Palette.Any(c => Convert.ToInt32(c.Substring(1, 2), 16) > 180 && Convert.ToInt32(c.Substring(3, 2), 16) < 90
                && Convert.ToInt32(c.Substring(5, 2), 16) < 90).ShouldBeTrue("the red picture is a third of the video");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Value(IReadOnlyList<string> arguments, string name)
    {
        var index = arguments.ToList().IndexOf(name);
        index.ShouldBeGreaterThanOrEqualTo(0, name);
        return arguments[index + 1];
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
