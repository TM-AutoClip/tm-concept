using Shouldly;
using TMConcept.Core.Media;
using TMConcept.Core.Music;
using TMConcept.Engines.Media;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The ffmpeg side of the laid-out music (0.1.45, F8): the analysis reads mono float samples; the bed is the entry-to-loop part split into its
/// copies and joined with one-beat cross-fades; music under only some scenes is cut, faded and delayed before its level envelope, and music
/// without a window keeps the 0.1.44 chain.
/// </summary>
public sealed class MusicBedTests
{
    [Fact]
    public void TheAnalysisReadsMonoFloatSamples()
    {
        var arguments = FfmpegMusicAudio.ReadArguments("song.mp3", 22050, 600, "a.f32");

        arguments.ShouldContain("f32le");
        string.Join(' ', arguments).ShouldContain("-t 600 -i song.mp3 -vn -ac 1 -ar 22050");
    }

    [Fact]
    public void AOnePartBedIsCutFromItsEntry()
    {
        var arguments = FfmpegMusicAudio.BedArguments("song.mp3", new MusicBed(2.5, 42.5, 1, 0, 40), "bed.flac");
        var filter = arguments[arguments.ToList().IndexOf("-filter_complex") + 1];

        filter.ShouldContain("atrim=start=2.5:end=42.5");
        filter.ShouldNotContain("acrossfade");
        filter.ShouldEndWith("[bed]atrim=duration=40[out]");
        arguments.ShouldContain("flac");
    }

    [Fact]
    public void ALoopedBedJoinsEveryCopyWithACrossFade()
    {
        var bed = new MusicBed(0.5, 28.5, 4, 0.5, 95);
        var arguments = FfmpegMusicAudio.BedArguments("song.mp3", bed, "bed.flac");
        var filter = arguments[arguments.ToList().IndexOf("-filter_complex") + 1];

        filter.ShouldContain("asplit=4[c0][c1][c2][c3]");
        filter.ShouldContain("[c0][c1]acrossfade=d=0.5:c1=tri:c2=tri[x1]");
        filter.ShouldContain("[x1][c2]acrossfade=d=0.5:c1=tri:c2=tri[x2]");
        filter.ShouldContain("[x2][c3]acrossfade=d=0.5:c1=tri:c2=tri[bed]");
        filter.ShouldEndWith("[bed]atrim=duration=95.5[out]");
        bed.TotalSeconds.ShouldBe(110.5);
    }

    [Fact]
    public void MusicUnderSomeScenesIsCutFadedAndDelayed()
    {
        var chain = FfmpegMuxer.MusicChain(new MuxMusic("bed.flac", false, 60, "0.5", 1.5, StartSeconds: 10, EndSeconds: 40, FadeInSeconds: 1.5));

        chain.ShouldStartWith("aresample=48000,aformat=channel_layouts=stereo,atrim=duration=30,asetpts=PTS-STARTPTS");
        chain.ShouldContain(",afade=t=in:st=0:d=1.5");
        chain.ShouldContain(",afade=t=out:st=28.5:d=1.5");
        chain.ShouldContain(",adelay=delays=10000:all=1");
        chain.ShouldEndWith(",volume='0.5':eval=frame");

        // Without a window the 0.1.44 chain is unchanged.
        FfmpegMuxer.MusicChain(new MuxMusic("m.wav", false, 6, "0.5", 2))
            .ShouldBe("aresample=48000,aformat=channel_layouts=stereo,atrim=duration=6,asetpts=PTS-STARTPTS,volume='0.5':eval=frame,afade=t=out:st=4:d=2");
    }
}
