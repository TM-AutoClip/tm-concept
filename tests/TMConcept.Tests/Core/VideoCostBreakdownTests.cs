using Shouldly;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Videos;

namespace TMConcept.Tests.Core;

public sealed class VideoCostBreakdownTests
{
    [Fact]
    public void TheConceptShareIsTheRunCostDividedByTheConceptsItKept()
    {
        VideoCostBreakdown.ConceptShare(0.11m, 5).ShouldBe(0.022m);
        VideoCostBreakdown.ConceptShare(0.10m, 3).ShouldBe(0.033333m);
        VideoCostBreakdown.ConceptShare(0.10m, 0).ShouldBe(0);
    }

    [Fact]
    public void LinesFollowTheWorkAndAddUpEachStep()
    {
        var video = Guid.NewGuid();
        VideoCost Cost(string step, decimal usd, bool exact = true) => new() { VideoId = video, Step = step, Usd = usd, Exact = exact };

        var lines = VideoCostBreakdown.Lines(
        [
            Cost(PipelineSteps.VideoCode, 0.30m),
            Cost(PipelineSteps.Images, 0.01m, exact: false),
            Cost(PipelineSteps.Speech, 0.02m, exact: false),
            Cost(PipelineSteps.VideoCode, 0.20m),
            Cost(PipelineSteps.ImagePromptCost, 0.004m),
            Cost(PipelineSteps.Images, 0.01m, exact: false),
            Cost(PipelineSteps.Concepts, 0.022m),
        ]);

        lines.Select(l => l.Step).ShouldBe(
        [
            PipelineSteps.Concepts, VideoCostBreakdown.NarrationText, PipelineSteps.ImagePromptCost, PipelineSteps.Images, PipelineSteps.Speech,
            PipelineSteps.VideoCode,
        ]);
        lines.Single(l => l.Step == PipelineSteps.VideoCode).ShouldBe(new VideoCostLine(PipelineSteps.VideoCode, 0.50m, true, 2));
        lines.Single(l => l.Step == PipelineSteps.Images).ShouldBe(new VideoCostLine(PipelineSteps.Images, 0.02m, false, 2));
        lines.Single(l => l.Step == VideoCostBreakdown.NarrationText).Usd.ShouldBe(0);
    }

    [Fact]
    public void AnUnknownStepComesLastAndUnusedStepsAreLeftOut()
    {
        var lines = VideoCostBreakdown.Lines([new VideoCost { Step = "later-step", Usd = 1 }, new VideoCost { Step = PipelineSteps.VideoCode, Usd = 1 }]);

        lines.Select(l => l.Step).ShouldBe([VideoCostBreakdown.NarrationText, PipelineSteps.VideoCode, "later-step"]);
    }
}
