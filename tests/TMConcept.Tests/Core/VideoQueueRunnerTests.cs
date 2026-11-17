using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Videos;

namespace TMConcept.Tests.Core;

/// <summary>
/// The queue's stop rules of 0.1.18 on a fake clock: night mode off stops at the first failure; night mode on goes on after a failure, waits
/// 10 s between videos and stops after three failures in a row (a made video starts the count again); the checkbox counts when it changes
/// during the run; the memory question's Cancel stops in both modes.
/// </summary>
public sealed class VideoQueueRunnerTests
{
    private sealed class Script(params QueueItemOutcome[] outcomes)
    {
        private readonly Queue<QueueItemOutcome> _outcomes = new(outcomes);

        public List<string> Made { get; } = [];

        public List<DateTimeOffset> StartedAt { get; } = [];

        public Action<int>? After { get; init; }

        public Task<string?> NextAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(_outcomes.Count > 0 ? "video " + (Made.Count + 1) : null);

        public Task<QueueItemOutcome> ProduceAsync(string item, CancellationToken cancellationToken, TimeProvider clock)
        {
            Made.Add(item);
            StartedAt.Add(clock.GetUtcNow());
            var outcome = _outcomes.Dequeue();
            After?.Invoke(Made.Count);
            return Task.FromResult(outcome);
        }
    }

    private static Task<QueueRunResult> Run(Script script, CountingTimeProvider clock, Func<bool> night, CancellationToken token = default) =>
        new VideoQueueRunner(clock, NullLogger<VideoQueueRunner>.Instance).RunAsync(script.NextAsync, (i, t) => script.ProduceAsync(i, t, clock), night, token);

    /// <summary>Moves the clock by the night gap each time the runner waits, until it ends.</summary>
    private static async Task<QueueRunResult> DriveAsync(Task<QueueRunResult> run, CountingTimeProvider clock)
    {
        var seen = 0;
        while (!run.IsCompleted)
        {
            if (clock.Timers > seen)
            {
                seen = clock.Timers;
                clock.Advance(VideoQueueRunner.NightGap);
            }

            await Task.Delay(2);
        }

        return await run;
    }

    [Fact]
    public async Task NightModeOffStopsTheWholeQueueAtTheFirstFailure()
    {
        var clock = new CountingTimeProvider();
        var script = new Script(QueueItemOutcome.Made, QueueItemOutcome.Failed, QueueItemOutcome.Made);

        var result = await Run(script, clock, () => false, TestContext.Current.CancellationToken);

        result.ShouldBe(new QueueRunResult(QueueRunEnd.Failed, 1, 1));
        script.Made.Count.ShouldBe(2, "the third video is not started");
        clock.Timers.ShouldBe(0, "no gap without night mode");
    }

    [Fact]
    public async Task NightModeGoesOnAfterAFailureAndStopsAfterThreeInARow()
    {
        var clock = new CountingTimeProvider();
        // A made video between failures starts the count again: F F M F F F stops at the sixth.
        var script = new Script(QueueItemOutcome.Failed, QueueItemOutcome.Failed, QueueItemOutcome.Made, QueueItemOutcome.Failed, QueueItemOutcome.Failed,
            QueueItemOutcome.Failed, QueueItemOutcome.Made);

        var result = await DriveAsync(Run(script, clock, () => true, TestContext.Current.CancellationToken), clock);

        result.ShouldBe(new QueueRunResult(QueueRunEnd.FailuresInRow, 1, 5));
        script.Made.Count.ShouldBe(6);
    }

    [Fact]
    public async Task NightModeWaitsTenSecondsBetweenVideos()
    {
        var clock = new CountingTimeProvider();
        var start = clock.GetUtcNow();
        var script = new Script(QueueItemOutcome.Made, QueueItemOutcome.Failed, QueueItemOutcome.Made);
        var run = Run(script, clock, () => true, TestContext.Current.CancellationToken);

        script.Made.Count.ShouldBe(1, "the first video starts at once");
        await clock.AdvanceAfterTimerAsync(1, TimeSpan.FromSeconds(9.9));
        script.Made.Count.ShouldBe(1, "not before 10 s");
        clock.Advance(TimeSpan.FromSeconds(0.1));
        await clock.AdvanceAfterTimerAsync(2, TimeSpan.FromSeconds(10));
        await clock.AdvanceAfterTimerAsync(3, TimeSpan.FromSeconds(10));

        (await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).ShouldBe(new QueueRunResult(QueueRunEnd.Done, 2, 1));
        script.StartedAt.ShouldBe([start, start.AddSeconds(10), start.AddSeconds(20)]);
    }

    [Fact]
    public async Task NightModeTickedDuringTheRunCountsFromThere()
    {
        // The user ticks night mode after the second video (going to bed): the third fails and the queue goes on; with it off it would stop.
        var clock = new CountingTimeProvider();
        var night = false;
        var script = new Script(QueueItemOutcome.Made, QueueItemOutcome.Made, QueueItemOutcome.Failed, QueueItemOutcome.Made)
        {
            After = n => night |= n == 2,
        };

        var result = await DriveAsync(Run(script, clock, () => night, TestContext.Current.CancellationToken), clock);

        result.ShouldBe(new QueueRunResult(QueueRunEnd.Done, 3, 1));
        clock.Timers.ShouldBe(3, "a gap before the third and the fourth video and before the empty queue");
    }

    [Fact]
    public async Task NightModeClearedDuringTheRunStopsAtTheNextFailure()
    {
        var clock = new CountingTimeProvider();
        var night = true;
        var script = new Script(QueueItemOutcome.Failed, QueueItemOutcome.Failed, QueueItemOutcome.Made)
        {
            After = n => night &= n != 2,
        };

        var result = await DriveAsync(Run(script, clock, () => night, TestContext.Current.CancellationToken), clock);

        result.End.ShouldBe(QueueRunEnd.Failed, "the first failure went on; cleared during the second video, its failure stops the queue");
        script.Made.Count.ShouldBe(2);
    }

    [Fact]
    public async Task TheMemoryQuestionsCancelStopsTheQueueInBothModes()
    {
        foreach (var night in new[] { false, true })
        {
            var clock = new CountingTimeProvider();
            var script = new Script(QueueItemOutcome.Made, QueueItemOutcome.Stopped, QueueItemOutcome.Made);
            var result = await DriveAsync(Run(script, clock, () => night, TestContext.Current.CancellationToken), clock);
            result.End.ShouldBe(QueueRunEnd.Stopped);
            script.Made.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task TheQueuesCancelEndsTheNightGap()
    {
        var clock = new CountingTimeProvider();
        using var cancel = new CancellationTokenSource();
        var script = new Script(QueueItemOutcome.Made, QueueItemOutcome.Made);
        var run = Run(script, clock, () => true, cancel.Token);

        await CountingTimeProvider.UntilAsync(() => clock.Timers == 1);
        await cancel.CancelAsync();

        (await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).End.ShouldBe(QueueRunEnd.Canceled);
        script.Made.Count.ShouldBe(1);
    }
}
