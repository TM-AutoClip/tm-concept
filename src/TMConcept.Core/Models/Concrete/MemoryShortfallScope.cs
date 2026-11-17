namespace TMConcept.Core.Models;

/// <summary>
/// Whether the current flow runs unattended (the video queue in night mode): then a memory shortfall is not asked about
/// (<see cref="IMemoryWaiter"/>), it fails the video. The queue opens a scope around each video with a function that reads the night mode
/// checkbox, so ticking or clearing it while the queue runs counts at the next decision. Flows outside a scope are attended.
/// </summary>
public static class MemoryShortfallScope
{
    private static readonly AsyncLocal<Func<bool>?> Current = new();

    /// <summary>Whether nobody should be asked now.</summary>
    public static bool IsUnattended => Current.Value?.Invoke() == true;

    /// <summary>
    /// Opens a scope for the calling flow (call it outside an async method's own body, or await the work inside the using): every await that
    /// follows in this flow sees <paramref name="unattended"/>; disposing it restores the scope before.
    /// </summary>
    public static IDisposable Begin(Func<bool> unattended)
    {
        ArgumentNullException.ThrowIfNull(unattended);
        var before = Current.Value;
        Current.Value = unattended;
        return new Restore(before);
    }

    private sealed class Restore(Func<bool>? before) : IDisposable
    {
        public void Dispose() => Current.Value = before;
    }
}
