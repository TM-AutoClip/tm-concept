using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Videos;

namespace TMConcept.Tests.Core;

/// <summary>The progress of a video run: its plan, the money it paid so far, the code call's line and a share that never goes back.</summary>
public sealed class VideoStepProgressTests
{
    private static (VideoStepProgress Steps, List<VideoProgress> Reports) Make(params (string Step, double Weight)[] steps)
    {
        var reports = new List<VideoProgress>();
        return (new VideoStepProgress(steps, new ForwardProgress<VideoProgress>(reports.Add)), reports);
    }

    [Fact]
    public void ThePlanIsTheStepsInOrderAndFinishingOneRunsTheNext()
    {
        var (steps, reports) = Make((VideoSteps.Prepare, 2), (VideoSteps.Code, 15), (VideoSteps.Render, 40));

        steps.Plan.ShouldBe([VideoSteps.Prepare, VideoSteps.Code, VideoSteps.Render]);
        steps.Report(VideoSteps.Prepare, 0);
        steps.Finish(VideoSteps.Prepare);
        reports[^1].Step.ShouldBe(VideoSteps.Code, "the steps follow each other");
        reports[^1].Plan.ShouldBe(steps.Plan);
        steps.Finish(VideoSteps.Code);
        steps.Finish(VideoSteps.Render);
        reports[^1].Step.ShouldBeEmpty("nothing runs after the last step");
        reports[^1].Share.ShouldBe(1);
    }

    [Fact]
    public void SpendingAddsUpAndIsReportedAtOnce()
    {
        var (steps, reports) = Make((VideoSteps.Speech, 1), (VideoSteps.Images, 1));
        var id = Guid.NewGuid();
        steps.Report(VideoSteps.Speech, 0.5, done: 1, total: 2);

        steps.Spend(new VideoCost { VideoId = id, Step = "speech", Usd = 0.012m });
        steps.Spend(new VideoCost { VideoId = id, Step = "images", Usd = 0.03m });

        steps.SpentUsd.ShouldBe(0.042m);
        steps.Spent.Count.ShouldBe(2);
        reports[^1].SpentUsd.ShouldBe(0.042m);
        (reports[^1].Step, reports[^1].Done, reports[^1].Total).ShouldBe((VideoSteps.Speech, 1, 2), "a spend keeps the step and its scenes");
    }

    [Fact]
    public void TheCodeCallPassesThroughUntilAnotherStepRuns()
    {
        var (steps, reports) = Make((VideoSteps.Code, 1), (VideoSteps.Render, 1));
        var call = new ChatProgress(ChatPhase.Writing, TimeSpan.FromSeconds(4), 1200, "x");
        steps.Report(VideoSteps.Code, 0.1);

        steps.Call(call);

        reports[^1].Call.ShouldBe(call);
        reports[^1].Step.ShouldBe(VideoSteps.Code);
        steps.Finish(VideoSteps.Code);
        reports[^1].Call.ShouldBeNull();
        steps.Report(VideoSteps.Render, 0.5, "frame 40/80");
        (reports[^1].Detail, reports[^1].Call).ShouldBe(("frame 40/80", (ChatProgress?)null));
    }

    [Fact]
    public void TheShareNeverGoesBack()
    {
        var (steps, reports) = Make((VideoSteps.Prepare, 2), (VideoSteps.Speech, 20), (VideoSteps.Timing, 1), (VideoSteps.Render, 40));
        steps.Report(VideoSteps.Prepare, 0);
        steps.Finish(VideoSteps.Prepare);
        for (var i = 1; i <= 4; i++)
        {
            steps.Report(VideoSteps.Speech, i / 4.0, done: i, total: 4);
            steps.Spend(new VideoCost { Step = "speech", Usd = 0.001m });
        }

        steps.Finish(VideoSteps.Speech);
        steps.Report(VideoSteps.Timing, 0);
        steps.Finish(VideoSteps.Timing);
        steps.Report(VideoSteps.Render, 0.3);
        steps.Report(VideoSteps.Render, 0.9);
        steps.Finish(VideoSteps.Render);

        reports.Select(r => r.Share).ShouldBeInOrder();
        reports.Select(r => r.SpentUsd).ShouldBeInOrder();
        reports[^1].Share.ShouldBe(1);
    }
}
