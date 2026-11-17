using Microsoft.Extensions.Time.Testing;

namespace TMConcept.Tests.Core;

/// <summary>
/// A fake clock that counts the timers made on it, so a test can wait until the code under test is really waiting (its <c>Task.Delay</c> made
/// a timer) before it moves the clock: no real time passes and no wait is skipped.
/// </summary>
internal sealed class CountingTimeProvider : FakeTimeProvider
{
    private int _timers;

    public CountingTimeProvider()
        : base(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public int Timers => Volatile.Read(ref _timers);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // Counted after the timer is registered: a test that sees the count may move the clock at once.
        var timer = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timers);
        return timer;
    }

    /// <summary>Waits (real time, at most 10 s) until <paramref name="done"/> holds.</summary>
    public static async Task UntilAsync(Func<bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!done())
        {
            if (DateTime.UtcNow > limit)
            {
                throw new TimeoutException("The condition did not hold within 10 s.");
            }

            await Task.Delay(2);
        }
    }

    /// <summary>Waits until the code made its <paramref name="count"/>th timer, then moves the clock by <paramref name="step"/>.</summary>
    public async Task AdvanceAfterTimerAsync(int count, TimeSpan step)
    {
        await UntilAsync(() => Timers >= count);
        Advance(step);
    }
}
