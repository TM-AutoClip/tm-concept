using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;
using TMConcept.Core.Media;
using TMConcept.Engines.Media;

namespace TMConcept.Tests.Engines;

/// <summary>
/// Background music at the mux (0.1.44, C3): music off gives exactly the command line of 0.1.43; a short track loops, a long one is cut,
/// both fade out at the end and get the ducking envelope as one more input of the narration's mix; ffprobe's answer for the music check.
/// </summary>
public sealed partial class MusicMuxTests
{
    private const string Envelope = "0.5-0.375*min(1,min(clip((t-0.85)/0.15,0,1),clip((3.4-t)/0.4,0,1)))";

    private static string Filter(IReadOnlyList<string> arguments) => arguments[arguments.ToList().IndexOf("-filter_complex") + 1];

    [Fact]
    public void MusicOffGivesTheCommandLineOfBefore()
    {
        // Written out as 0.1.43 built it: narration at the scene starts, subtitles with their fonts, one soft transition.
        string[] before =
        [
            "-hide_banner", "-loglevel", "error", "-y", "-i", "silent.mp4", "-i", "1.wav", "-i", "2.wav", "-filter_complex",
            "[0:v]ass=subtitles.ass:fontsdir=fonts,fade=t=out:st=3.95:d=0.25:enable='between(t,3.95,4.2)',"
            + "fade=t=in:st=4.2:d=0.25:enable='between(t,4.2,4.45)'[v];[1:a]aresample=48000,adelay=0|0[a0];"
            + "[2:a]aresample=48000,adelay=4200|4200[a1];[a0][a1]amix=inputs=2:normalize=0:dropout_transition=0[a]",
            "-map", "[v]", "-map", "[a]", "-c:a", "aac", "-b:a", "192k", "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p",
            "-movflags", "+faststart", "video.mp4",
        ];

        FfmpegMuxer.Arguments("silent.mp4", [("1.wav", 0), ("2.wav", 4200)], new MuxOverlay("subtitles.ass", "fonts"), [4200], "video.mp4")
            .ShouldBe(before);
        FfmpegMuxer.Arguments("silent.mp4", [("1.wav", 0), ("2.wav", 4200)], new MuxOverlay("subtitles.ass", "fonts", Music: null), [4200], "video.mp4")
            .ShouldBe(before);

        // Silent and plain: still only copied.
        FfmpegMuxer.Arguments("silent.mp4", [], MuxOverlay.None, [], "video.mp4").ShouldBe(
            ["-hide_banner", "-loglevel", "error", "-y", "-i", "silent.mp4", "-map", "0:v", "-c:v", "copy", "-movflags", "+faststart", "video.mp4"]);
    }

    [Fact]
    public void AShortTrackLoopsAndIsCutAtTheVideosEndWithAFadeOut()
    {
        var music = new MuxMusic("C:\\work\\music\\song.mp3", Loop: true, DurationSeconds: 10, Envelope, FadeOutSeconds: 2);

        var arguments = FfmpegMuxer.Arguments("silent.mp4", [("1.wav", 0), ("2.wav", 4200)], new MuxOverlay("subtitles.ass", Music: music), [], "video.mp4");

        var input = arguments.ToList().IndexOf("C:\\work\\music\\song.mp3");
        arguments[input - 1].ShouldBe("-i");
        arguments[input - 3].ShouldBe("-stream_loop");
        arguments[input - 2].ShouldBe("-1", "the track plays again from its start");
        var filter = Filter(arguments);
        filter.ShouldContain("[3:a]aresample=48000,aformat=channel_layouts=stereo,atrim=duration=10,asetpts=PTS-STARTPTS,volume='" + Envelope
            + "':eval=frame,afade=t=out:st=8:d=2[music]");
        filter.ShouldEndWith("[a0][a1][music]amix=inputs=3:normalize=0:dropout_transition=0[a]");
        filter.ShouldContain("[2:a]aresample=48000,adelay=4200|4200[a1]", customMessage: "the narration is placed as before");
        arguments.ShouldContain("[a]");
    }

    [Fact]
    public void ALongTrackIsOnlyCutWithAFadeOut()
    {
        var music = new MuxMusic("long.mp3", Loop: false, DurationSeconds: 7.5, "0.5", FadeOutSeconds: 3);

        var arguments = FfmpegMuxer.Arguments("silent.mp4", [("1.wav", 0)], new MuxOverlay(null, Music: music), [], "video.mp4");

        arguments.ShouldNotContain("-stream_loop");
        var filter = Filter(arguments);
        filter.ShouldContain("[2:a]aresample=48000,aformat=channel_layouts=stereo,atrim=duration=7.5,asetpts=PTS-STARTPTS,volume='0.5':eval=frame,"
            + "afade=t=out:st=4.5:d=3[music]");
        filter.ShouldContain("amix=inputs=2");
        arguments.ShouldContain("copy", "nothing is drawn: the picture is copied, only the sound is new");
    }

    [Fact]
    public void WithoutNarrationTheMusicIsTheWholeSoundAndAfterThePhotoInput()
    {
        var music = new MuxMusic("m.wav", Loop: false, DurationSeconds: 6, "0.5", FadeOutSeconds: 0);
        var photo = new MuxPhoto("channel-photo.png", 10, 10, 100, 4, 6, 0.5, 30);

        var arguments = FfmpegMuxer.Arguments("silent.mp4", [], new MuxOverlay(null, Photo: photo, Music: music), [], "video.mp4");

        var filter = Filter(arguments);
        filter.ShouldContain("[1:v]scale=", customMessage: "the photo keeps its input number");
        filter.ShouldContain("[2:a]aresample=48000,aformat=channel_layouts=stereo,atrim=duration=6,asetpts=PTS-STARTPTS,volume='0.5':eval=frame[a]");
        filter.ShouldNotContain("amix");
        filter.ShouldNotContain("afade", customMessage: "no fade-out asked");
        arguments[arguments.ToList().IndexOf("[a]") - 1].ShouldBe("-map");
        arguments.ShouldContain("aac");
    }

    [Fact]
    public void TheFadeIsNeverLongerThanTheVideo() =>
        FfmpegMuxer.MusicChain(new MuxMusic("m.wav", false, 1.5, "1", 4)).ShouldEndWith(",afade=t=out:st=0:d=1.5");

    [Fact]
    public void FfprobesAnswerGivesTheLengthOnlyForSound()
    {
        FfmpegMedia.ProbeDuration(["codec_type=audio", "duration=12.345678"]).ShouldBe(12346);
        FfmpegMedia.ProbeDuration(["codec_type=video", "codec_type=audio", "duration=3.000000"]).ShouldBe(3000, "a music video's sound");
        FfmpegMedia.ProbeDuration(["codec_type=video", "duration=3.000000"]).ShouldBeNull("a picture without sound");
        FfmpegMedia.ProbeDuration(["codec_type=audio", "duration=N/A"]).ShouldBeNull("no length");
        FfmpegMedia.ProbeDuration([]).ShouldBeNull("ffprobe said nothing: a damaged file");
        FfmpegMedia.ProbeArguments("a.mp3").ShouldBe(["-v", "error", "-show_entries", "stream=codec_type:format=duration", "-of", "default=noprint_wrappers=1", "a.mp3"]);
    }

    /// <summary>
    /// A real ffmpeg (PATH or TMCONCEPT_TEST_FFMPEG; skipped without one): a 1 s tone under a 4 s video loops to the end, and the envelope
    /// makes it clearly quieter in the narration part (1–3 s) than before it.
    /// </summary>
    [Fact]
    public async Task ARealFfmpegLoopsDucksAndCutsTheMusic()
    {
        var ffmpeg = PlaybackTests.Ffmpeg();
        if (ffmpeg is null)
        {
            Assert.Skip("ffmpeg was not found (PATH or TMCONCEPT_TEST_FFMPEG).");
        }

        var folder = Path.Combine(TestFolders.Root, "music-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var silent = Path.Combine(folder, "silent.mp4");
            await RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=black:size=64x36:rate=30:duration=4",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", silent]);
            var tone = Path.Combine(folder, "tone.wav");
            await RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", tone]);
            var spans = MusicEnvelope.Merge([new NarrationSpan(1000, 3000)]);
            var music = new MuxMusic(tone, Loop: true, DurationSeconds: 4, MusicEnvelope.Expression(spans, 0.5, 0.125), FadeOutSeconds: 0.5);
            var output = Path.Combine(folder, "video.mp4");

            await RunAsync(ffmpeg, [.. FfmpegMuxer.Arguments(silent, [], new MuxOverlay(null, Music: music), [], output)]);

            var before = await MeanVolumeAsync(ffmpeg, output, 0.2, 0.5);
            var under = await MeanVolumeAsync(ffmpeg, output, 1.5, 1.0);
            var after = await MeanVolumeAsync(ffmpeg, output, 3.45, 0.05);
            (before - under).ShouldBeInRange(9, 15, "about 12 dB down under the narration");
            before.ShouldBeGreaterThan(-60, "the looped tone still sounds after its first second");
            after.ShouldBeGreaterThan(under, "back up after the narration");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<double> MeanVolumeAsync(string ffmpeg, string video, double from, double seconds)
    {
        var c = CultureInfo.InvariantCulture;
        var error = await RunAsync(ffmpeg, ["-hide_banner", "-ss", from.ToString("0.###", c), "-t", seconds.ToString("0.###", c), "-i", video,
            "-af", "volumedetect", "-vn", "-f", "null", "-"]);
        var match = MeanVolume().Match(error);
        match.Success.ShouldBeTrue(error);
        return double.Parse(match.Groups[1].Value, c);
    }

    private static async Task<string> RunAsync(string ffmpeg, IReadOnlyList<string> arguments)
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
        return error;
    }

    [GeneratedRegex(@"mean_volume:\s*(-?[\d.]+) dB")]
    private static partial Regex MeanVolume();
}
