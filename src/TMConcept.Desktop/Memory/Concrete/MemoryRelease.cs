using System.Globalization;
using System.Runtime;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Memory;

/// <summary>
/// Gives memory back to the system after a large piece of work ends (a video job, closing the player): one blocking full collection that
/// also compacts the large object heap (frame buffers, decoded pictures and long model answers live there) and returns the freed pages to
/// the system. It takes some tens of milliseconds, so it runs only at those moments, never per frame. Small machines (a Raspberry Pi) keep
/// the memory of a finished job otherwise.
/// </summary>
public static class MemoryRelease
{
    /// <summary>Collects now and logs the managed heap before and after.</summary>
    public static void Now(ILogger logger, string reason)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var before = GC.GetTotalMemory(forceFullCollection: false);
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        var after = GC.GetTotalMemory(forceFullCollection: false);
        logger.LogInformation("Memory given back after {Reason}: managed heap {Before} MB -> {After} MB", reason,
            (before / 1_048_576.0).ToString("0.0", CultureInfo.InvariantCulture), (after / 1_048_576.0).ToString("0.0", CultureInfo.InvariantCulture));
    }
}
