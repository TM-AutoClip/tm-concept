using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Models;

namespace TMConcept.Tests.Core;

/// <summary>
/// The memory check of 0.1.18 on a fake clock: 3 s after an unload the first reading, 2.5 s later the second, then the question; Retry waits
/// exactly 5 s; Cancel stops; night mode asks nobody; the queue's Cancel closes an open question. No real waits, no graphics card.
/// </summary>
public sealed class MemoryWaiterTests
{
    private static readonly MemoryShortfall Short = new("Z-Image Turbo", 6L << 30, 4L << 30, "PDH", 5L << 30, 2L << 30, 3L << 30);

    private static readonly IModel Model = new FakeLocalModel("z-image-turbo");

    private static MemoryWaiter Waiter(CountingTimeProvider clock, IMemoryShortfallHandler? handler, IMemoryNotices? notices = null) =>
        new(clock, MemoryWaitOptions.Default, NullLogger<MemoryWaiter>.Instance, handler, notices);

    [Fact]
    public async Task AfterAnUnloadItMeasuresAt3sAnd5Point5sThenAsks()
    {
        var clock = new CountingTimeProvider();
        var start = clock.GetUtcNow();
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.Cancel) { Clock = clock };
        var notices = new Engines.ImageBackendTests.RecordingNotices();
        var readings = new List<TimeSpan>();
        var waiting = Waiter(clock, handler, notices).WaitAsync(Model, start, _ =>
        {
            readings.Add(clock.GetUtcNow() - start);
            return Task.FromResult<MemoryShortfall?>(Short);
        }, CancellationToken.None);

        await clock.AdvanceAfterTimerAsync(1, TimeSpan.FromSeconds(2.9));
        readings.ShouldBeEmpty("nothing is read before 3 s");
        clock.Advance(TimeSpan.FromSeconds(0.1));
        await clock.AdvanceAfterTimerAsync(2, TimeSpan.FromSeconds(2.5));

        var error = await TestWaiters.CatchAsync<MemoryShortfallCanceledException>(() => waiting);
        readings.ShouldBe([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5.5)]);
        handler.AskedAt.ShouldBe([start + TimeSpan.FromSeconds(5.5)], "asked right after the second reading");
        handler.Asked.ShouldBe([Short]);
        error.Shortfall.ShouldBe(Short);
        notices.Kinds.ShouldBe([MemoryNoticeKind.Settling, MemoryNoticeKind.Measuring, MemoryNoticeKind.Measuring, MemoryNoticeKind.Asking,
            MemoryNoticeKind.Canceled]);
        notices.Notices.Where(n => n.Kind == MemoryNoticeKind.Measuring).Select(n => (n.Step, n.Of)).ShouldBe([(1, 2), (2, 2)]);
    }

    [Fact]
    public async Task EnoughAtTheSecondReadingAsksNothing()
    {
        var clock = new CountingTimeProvider();
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.Cancel);
        var reads = 0;
        var waiting = Waiter(clock, handler).WaitAsync(Model, null, _ => Task.FromResult(++reads == 1 ? Short : null), CancellationToken.None);

        reads.ShouldBe(1, "nothing was unloaded: the first reading is at once");
        await clock.AdvanceAfterTimerAsync(1, TimeSpan.FromSeconds(2.5));
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        reads.ShouldBe(2);
        handler.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task EnoughAtOnceWaitsForNothing()
    {
        var clock = new CountingTimeProvider();
        await Waiter(clock, null).WaitAsync(Model, null, _ => Task.FromResult<MemoryShortfall?>(null), CancellationToken.None);
        clock.Timers.ShouldBe(0);
    }

    [Fact]
    public async Task RetryWaitsExactlyFiveSecondsAndGoesOnWhenThereIsEnough()
    {
        var clock = new CountingTimeProvider();
        var start = clock.GetUtcNow();
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.Retry) { Clock = clock };
        var readings = new List<TimeSpan>();
        var waiting = Waiter(clock, handler).WaitAsync(Model, null, _ =>
        {
            readings.Add(clock.GetUtcNow() - start);
            return Task.FromResult(readings.Count < 3 ? Short : null);
        }, CancellationToken.None);

        await clock.AdvanceAfterTimerAsync(1, TimeSpan.FromSeconds(2.5));
        await clock.AdvanceAfterTimerAsync(2, TimeSpan.FromSeconds(4.9));
        readings.Count.ShouldBe(2, "not before 5 s after Retry");
        clock.Advance(TimeSpan.FromSeconds(0.1));
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        readings.ShouldBe([TimeSpan.Zero, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(7.5)]);
        handler.Asked.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RetryRetryCancelThrowsTheCancel()
    {
        var clock = new CountingTimeProvider();
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.Retry, MemoryShortfallChoice.Retry, MemoryShortfallChoice.Cancel);
        var reads = 0;
        var waiting = Waiter(clock, handler).WaitAsync(Model, null, _ =>
        {
            reads++;
            return Task.FromResult<MemoryShortfall?>(Short);
        }, CancellationToken.None);

        await clock.AdvanceAfterTimerAsync(1, TimeSpan.FromSeconds(2.5));
        await clock.AdvanceAfterTimerAsync(2, TimeSpan.FromSeconds(5));
        await clock.AdvanceAfterTimerAsync(3, TimeSpan.FromSeconds(5));

        await TestWaiters.CatchAsync<MemoryShortfallCanceledException>(() => waiting);
        handler.Asked.Count.ShouldBe(3, "the user drives the loop: no limit of its own");
        reads.ShouldBe(4);
    }

    [Fact]
    public async Task InNightModeNobodyIsAskedAndTheUseFails()
    {
        var clock = new CountingTimeProvider();
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.Retry);
        var notices = new Engines.ImageBackendTests.RecordingNotices();
        var night = true;
        Task waiting;
        using (MemoryShortfallScope.Begin(() => night))
        {
            waiting = Waiter(clock, handler, notices).WaitAsync(Model, null, _ => Task.FromResult<MemoryShortfall?>(Short), CancellationToken.None);
        }

        await clock.AdvanceAfterTimerAsync(1, TimeSpan.FromSeconds(2.5));
        var error = await Should.ThrowAsync<MemoryShortfallException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        error.Shortfall.ShouldBe(Short);
        error.Message.ShouldContain("Z-Image Turbo");
        handler.Asked.ShouldBeEmpty();
        notices.Kinds[^1].ShouldBe(MemoryNoticeKind.Failed);
        MemoryShortfallScope.IsUnattended.ShouldBeFalse("the scope ended");
    }

    [Fact]
    public async Task NightModeIsReadWhenTheShortfallHappens()
    {
        // Ticked after the work started (the user goes to bed at video 50): the next shortfall asks nobody.
        var clock = new CountingTimeProvider();
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.Retry);
        var night = false;
        Task waiting;
        using (MemoryShortfallScope.Begin(() => night))
        {
            waiting = Waiter(clock, handler).WaitAsync(Model, null, _ => Task.FromResult<MemoryShortfall?>(Short), CancellationToken.None);
        }

        night = true;
        await clock.AdvanceAfterTimerAsync(1, TimeSpan.FromSeconds(2.5));
        await Should.ThrowAsync<MemoryShortfallException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        handler.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheQueuesCancelClosesAnOpenQuestion()
    {
        var clock = new CountingTimeProvider();
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.Retry) { Hold = true };
        using var cancel = new CancellationTokenSource();
        var waiting = Waiter(clock, handler).WaitAsync(Model, null, _ => Task.FromResult<MemoryShortfall?>(Short), cancel.Token);

        await clock.AdvanceAfterTimerAsync(1, TimeSpan.FromSeconds(2.5));
        await CountingTimeProvider.UntilAsync(() => handler.Asked.Count == 1);
        await cancel.CancelAsync();

        var error = await Should.ThrowAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        error.ShouldNotBeOfType<MemoryShortfallCanceledException>("the queue's own Cancel, not the question's");
    }
}
