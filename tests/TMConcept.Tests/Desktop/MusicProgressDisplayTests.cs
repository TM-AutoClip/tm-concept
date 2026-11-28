using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Core.Music;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.61 (plan K14/K15): a music model's progress on screen, the same on the music page and in the video's step row: "Aşama 2/3 · blok 16/40 ·
/// %40" (TR and EN), a filling bar for a counted stage, a moving one with the time so far for a model that counts nothing (an online model,
/// ACE-Step). No model runs.
/// </summary>
public sealed class MusicProgressDisplayTests
{
    private static readonly MusicProgress Blocks = new(2, 3, 16, 40, MusicProgressUnit.Blocks);

    [Theory]
    [InlineData("tr", "Aşama 2/3 · blok 16/40 · %40")]
    [InlineData("en", "Stage 2/3 · block 16/40 · 40 %")]
    public void ACountedStageReadsWithItsStageCountAndPercentage(string language, string expected)
    {
        var text = new Localizer();
        text.Use(language);
        var labels = new MusicProgressText(text);

        labels.Line(Blocks).ShouldBe(expected);
        labels.WithTime(Blocks, TimeSpan.FromSeconds(192)).ShouldBe(expected + " · 3:12");
    }

    [Fact]
    public void AStageWithoutACountAndAModelThatCountsNothingReadWithoutAPercentage()
    {
        var text = new Localizer();
        text.Use("tr");
        var labels = new MusicProgressText(text);

        labels.Line(new MusicProgress(3, 3, 0, 0, MusicProgressUnit.Decoding)).ShouldBe("Aşama 3/3 · çözülüyor");
        labels.Line(new MusicProgress(1, 2, 0, 0, MusicProgressUnit.Tokens)).ShouldBe("Aşama 1/2", "the length is not known yet");
        labels.Line(new MusicProgress(1, 1, 120, 1503, MusicProgressUnit.Tokens)).ShouldBe("Aşama 1/1 · jeton 120/1503 · %7");
        labels.Line(MusicProgress.Unknown).ShouldBeEmpty();
        labels.WithTime(MusicProgress.Unknown, TimeSpan.FromSeconds(42)).ShouldBe("geçen süre 0:42", "an online model: the time so far");
        labels.Video(MusicProgress.Unknown).ShouldBe("Müzik üretiliyor");
        labels.Video(Blocks).ShouldBe("Müzik: Aşama 2/3 · blok 16/40 · %40");
        foreach (var unit in Enum.GetValues<MusicProgressUnit>().Where(u => u != MusicProgressUnit.None))
        {
            labels.Line(new MusicProgress(1, 1, 1, 2, unit)).ShouldNotContain("music.progress", Case.Sensitive, unit + " has its text");
        }
    }

    [AvaloniaFact]
    public void TheBarFillsForACountedStageAndMovesWithoutAShareOtherwise()
    {
        var text = new Localizer();
        text.Use("tr");
        var bar = new MusicProgressViewModel(new MusicProgressText(text));

        bar.Begin();
        bar.Active.ShouldBeTrue();
        bar.Indeterminate.ShouldBeTrue("nothing counted yet");
        bar.Text.ShouldStartWith("geçen süre 0:0");

        bar.Reporter.Report(Blocks);
        bar.Counted.ShouldBeTrue();
        bar.Percent.ShouldBe(40);
        bar.Text.ShouldStartWith("Aşama 2/3 · blok 16/40 · %40 · 0:0");

        bar.Report(new MusicProgress(3, 3, 0, 0, MusicProgressUnit.Decoding));
        bar.Indeterminate.ShouldBeTrue("the decoding has no count");

        bar.End();
        bar.Active.ShouldBeFalse();
        bar.Report(Blocks);
        bar.Text.ShouldBeEmpty("a late line of a stopped script changes nothing");
    }

    [AvaloniaFact]
    public async Task TheVideosStepRowShowsTheMusicLineWhileItIsMadeAndDropsItAfter()
    {
        using var h = new DesktopHarness();
        h.Get<Localizer>().Use("tr");
        var progress = h.Get<Func<ProductionProgressViewModel>>()();
        IReadOnlyList<string> plan = [VideoSteps.Prepare, VideoSteps.Timing, VideoSteps.Code];
        progress.Begin("work.video", string.Empty);

        progress.Report(new VideoProgress(0.2, VideoSteps.Timing, string.Empty, 0, plan, Music: MusicProgress.Unknown));
        var timing = progress.Stages.Single(s => s.Code == VideoSteps.Timing);
        timing.Detail.ShouldBe("Müzik üretiliyor");
        progress.Report(new VideoProgress(0.3, VideoSteps.Timing, string.Empty, 0, plan, Music: Blocks));
        timing.Detail.ShouldBe("Müzik: Aşama 2/3 · blok 16/40 · %40");

        progress.Report(new VideoProgress(0.5, VideoSteps.Code, string.Empty, 0, plan));
        timing.Detail.ShouldBeEmpty("the piece is made: its line does not stay under the finished step");
        await progress.FinishAsync(ok: true);
    }
}
