using Microsoft.Extensions.Logging.Abstractions;
using TMConcept.Core.Models;

namespace TMConcept.Tests.Core;

/// <summary>Memory checks for tests that are not about the waits: every wait 1 ms, and nobody to ask (a shortfall fails at once).</summary>
internal static class TestWaiters
{
    /// <summary>1 ms for every wait.</summary>
    public static readonly MemoryWaitOptions FastOptions = new(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));

    /// <summary>
    /// Awaits <paramref name="work"/> and returns the exception of type <typeparamref name="TException"/> it threw (awaited for real: a
    /// cancellation's own type, such as <see cref="MemoryShortfallCanceledException"/>, is kept by await but not by a continuation).
    /// </summary>
    public static async Task<TException> CatchAsync<TException>(Func<Task> work)
        where TException : Exception
    {
        try
        {
            await work();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new Shouldly.ShouldAssertException($"{typeof(TException).Name} was expected, but nothing was thrown.");
    }

    /// <summary>A waiter with <see cref="FastOptions"/> on the system clock.</summary>
    public static MemoryWaiter Fast(IMemoryShortfallHandler? handler = null, IMemoryNotices? notices = null) =>
        new(TimeProvider.System, FastOptions, NullLogger<MemoryWaiter>.Instance, handler, notices);
}
