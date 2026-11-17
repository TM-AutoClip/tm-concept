namespace TMConcept.Core.Models;

/// <summary>The waits of a memory check (<see cref="MemoryWaiter"/>; the developer's numbers of 17 Nov 2026, not settings).</summary>
/// <param name="Settle">After a model was unloaded: the wait before the first reading (drivers give memory back lazily).</param>
/// <param name="Recheck">Not enough at the first reading: the wait before the second one.</param>
/// <param name="Retry">After the user chose Retry: the wait before the next reading.</param>
public sealed record MemoryWaitOptions(TimeSpan Settle, TimeSpan Recheck, TimeSpan Retry)
{
    /// <summary>3 s after an unload, 2.5 s before the second reading, 5 s after Retry.</summary>
    public static MemoryWaitOptions Default { get; } = new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5));
}
