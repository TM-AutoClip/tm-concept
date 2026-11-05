using Shouldly;
using TMConcept.Engines.Media;

namespace TMConcept.Tests.Engines;

public sealed class MuxTests
{
    [Fact]
    public void ClipsArePlacedAtTheirSceneStartsAndSubtitlesAreBurnedIn()
    {
        var arguments = FfmpegMuxer.Arguments("silent.mp4", [("1.wav", 0), ("2.wav", 4200)], "subtitles.ass", "video.mp4");

        var filter = arguments[arguments.ToList().IndexOf("-filter_complex") + 1];
        filter.ShouldContain("[0:v]ass=subtitles.ass[v]");
        filter.ShouldContain("[2:a]aresample=48000,adelay=4200|4200[a1]");
        filter.ShouldContain("amix=inputs=2:normalize=0");
        arguments.ShouldContain("libx264");
        arguments[^1].ShouldBe("video.mp4");
    }

    [Fact]
    public void WithoutNarrationAndSubtitlesTheVideoIsOnlyCopied()
    {
        var arguments = FfmpegMuxer.Arguments("silent.mp4", [], null, "video.mp4");

        arguments.ShouldNotContain("-filter_complex");
        arguments.ShouldContain("copy");
    }
}
