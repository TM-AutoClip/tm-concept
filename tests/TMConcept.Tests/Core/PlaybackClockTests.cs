using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.Playback;

namespace TMConcept.Tests.Core;

/// <summary>The player's clock and its audio–video sync rule, on a fake clock (no device, no ffmpeg).</summary>
public sealed class PlaybackClockTests
{
    private static long Bytes(double milliseconds) => (long)(milliseconds * PlaybackFormat.BytesPerMs);

    [Fact]
    public void WithoutSoundTheWallClockRunsFromTheStartAndAPauseFreezesIt()
    {
        var time = new FakeTimeProvider();
        var clock = new PlaybackClock(time);
        clock.Start(5_000, null);

        time.Advance(TimeSpan.FromMilliseconds(1_250));
        clock.PositionMs.ShouldBe(6_250, 0.5);

        clock.Freeze().ShouldBe(6_250);
        time.Advance(TimeSpan.FromSeconds(3));
        clock.PositionMs.ShouldBe(6_250, 0.5);

        // Play again (or seek) starts a new run at any position.
        clock.Start(1_000, null);
        time.Advance(TimeSpan.FromMilliseconds(100));
        clock.PositionMs.ShouldBe(1_100, 0.5);
    }

    [Fact]
    public void WithSoundTheClockFollowsThePlayedBytesNotTheWall()
    {
        var time = new FakeTimeProvider();
        var clock = new PlaybackClock(time);
        long played = 0;
        clock.Start(2_000, () => played);

        // The device has not started yet: the picture must wait, whatever the wall says.
        time.Advance(TimeSpan.FromMilliseconds(300));
        clock.PositionMs.ShouldBe(2_000, 0.5);

        played = Bytes(500);
        clock.PositionMs.ShouldBe(2_500, 0.5);
        time.Advance(TimeSpan.FromSeconds(10));
        clock.PositionMs.ShouldBe(2_500, 0.5);
    }

    [Fact]
    public void AfterTheSoundIsPlayedOutTheClockGoesOnWithTheWall()
    {
        var time = new FakeTimeProvider();
        var clock = new PlaybackClock(time);
        long played = 0;
        clock.Start(0, () => played);
        played = Bytes(800);
        clock.SoundEnded(Bytes(1_000));
        clock.PositionMs.ShouldBe(800, 0.5);

        played = Bytes(1_000);
        clock.PositionMs.ShouldBe(1_000, 0.5);
        time.Advance(TimeSpan.FromMilliseconds(400));
        clock.PositionMs.ShouldBe(1_400, 0.5);
    }

    [Theory]
    [InlineData(1_000, 1_000, FrameDecision.Show)]
    [InlineData(1_003, 1_000, FrameDecision.Show)]
    [InlineData(1_040, 1_000, FrameDecision.Wait)]
    [InlineData(1_000, 1_050, FrameDecision.Show)]
    [InlineData(1_000, 1_061, FrameDecision.Drop)]
    public void AFrameIsShownOnTimeWaitsWhenEarlyAndIsDroppedWhenLate(double frameMs, double clockMs, FrameDecision expected) =>
        FrameScheduler.Decide(frameMs, clockMs, 1000.0 / 30).ShouldBe(expected);

    [Fact]
    public void FrameTimesSizeAndRate()
    {
        FrameScheduler.FrameTime(1_000, 30, 30).ShouldBe(2_000, 0.001);
        FrameScheduler.Size(1080, 1920).ShouldBe((404, 720));
        FrameScheduler.Size(1920, 1080).ShouldBe((720, 404));
        FrameScheduler.Size(320, 240).ShouldBe((320, 240));
        FrameScheduler.Fps(60).ShouldBe(30);
        FrameScheduler.Fps(24).ShouldBe(24);
        FrameScheduler.Fps(0).ShouldBe(30);
    }

    [Fact]
    public void TheSilenceBeforeTheSoundIsNotTime()
    {
        var content = new AudioContentClock();
        content.Given(0, 4_800);
        content.Given(1_920, 0);
        content.Given(960, 960);

        content.ContentBytes(4_800).ShouldBe(0);
        content.ContentBytes(5_760).ShouldBe(960);
        content.ContentBytes(4_800 + 1_920 + 960).ShouldBe(2_880);
        content.ContentBytes(4_800 + 1_920 + 960 + 960).ShouldBe(2_880);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(7_400, "0:07")]
    [InlineData(179_999, "2:59")]
    [InlineData(3_723_000, "1:02:03")]
    [InlineData(-5, "0:00")]
    public void TimeText(long milliseconds, string expected) => PlaybackTime.Format(milliseconds).ShouldBe(expected);
}
