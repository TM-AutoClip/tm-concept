using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.Diagnostics;
using TMConcept.Tests.Documents;

namespace TMConcept.Tests.Core;

/// <summary>The step timer of the start and page timings (0.1.36), on a fake clock.</summary>
public sealed class StepTimerTests
{
    [Fact]
    public void NestedStepsAddUpAndLinesWaitForTheLog()
    {
        var time = new FakeTimeProvider();
        var timer = new StepTimer(time);
        using (timer.Step("page.concepts.activate", countConnections: true))
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            using (timer.Step("page.concepts.reload"))
            {
                time.Advance(TimeSpan.FromMilliseconds(50));
                timer.CountConnection(TimeSpan.FromMilliseconds(7));
            }

            using (timer.Step("page.concepts.reload"))
            {
                time.Advance(TimeSpan.FromMilliseconds(30));
                timer.CountConnection(TimeSpan.FromMilliseconds(5));
            }

            time.Advance(TimeSpan.FromMilliseconds(20));
        }

        // The inner step ran twice (its times add up); the outer one includes both.
        timer.Total("page.concepts.reload").ShouldBe(TimeSpan.FromMilliseconds(80));
        timer.Total("page.concepts.activate").ShouldBe(TimeSpan.FromMilliseconds(200));
        timer.Connections.ShouldBe(new ConnectionTotals(2, TimeSpan.FromMilliseconds(12)));

        // Measured before the log existed: written in their order once it is attached, then every new line at once.
        var log = new RecordingLogger<StepTimerTests>();
        timer.Attach(log);
        timer.Mark("startup.at.done");
        log.Lines.Count.ShouldBe(4);
        log.Lines[0].ShouldStartWith("Information: page.concepts.reload 50");
        log.Lines[1].ShouldStartWith("Information: page.concepts.reload 30");
        log.Lines[2].ShouldStartWith("Information: page.concepts.activate 200 (database: 2 connection(s) opened in 12 ms)");
        log.Lines[3].ShouldStartWith("Information: startup.at.done 200");
    }
}
