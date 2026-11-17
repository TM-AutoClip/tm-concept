namespace TMConcept.Core.Models;

/// <summary>
/// The one memory check before a local model uses the graphics card (0.1.18, the developer's rule): after a model was unloaded wait
/// <see cref="MemoryWaitOptions.Settle"/>, then measure; not enough: wait <see cref="MemoryWaitOptions.Recheck"/> and measure again; still not
/// enough: ask the user (<see cref="IMemoryShortfallHandler"/>): Retry waits <see cref="MemoryWaitOptions.Retry"/> and measures again (as often
/// as the user wants), Cancel throws <see cref="MemoryShortfallCanceledException"/> (the video queue stops). In night mode
/// (<see cref="MemoryShortfallScope"/>) nobody is asked: <see cref="MemoryShortfallException"/> fails the video. Every step is reported
/// (<see cref="IMemoryNotices"/>) and logged.
/// </summary>
public interface IMemoryWaiter
{
    /// <summary>Waits until <paramref name="measure"/> finds enough memory (it returns null) or the user gives up.</summary>
    /// <param name="model">The model about to use the card.</param>
    /// <param name="unloadedAt">When the previous model was unloaded (null = nothing was: the first reading is at once).</param>
    /// <param name="measure">Reads the memory now; the figures when it is not enough, null when it is.</param>
    /// <param name="cancellationToken">The caller's (the queue's) token: it ends a wait and closes an open question.</param>
    Task WaitAsync(IModel model, DateTimeOffset? unloadedAt, Func<CancellationToken, Task<MemoryShortfall?>> measure, CancellationToken cancellationToken);
}
