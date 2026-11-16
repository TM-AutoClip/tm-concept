namespace TMConcept.Core.Hardware;

/// <summary>
/// The graphics card memory at one moment (<see cref="IVramProbe"/>): the card's own memory and how much of it is free now.
/// </summary>
/// <param name="TotalBytes">The card's own memory (0 = no card, or unknown).</param>
/// <param name="FreeBytes">Free now: the smallest figure of the sources that could be read, or the estimate of <see cref="Estimate"/>.</param>
/// <param name="Measured">Whether a source measured it (false: <see cref="Estimate"/>).</param>
/// <param name="Source">Which source gave <see cref="FreeBytes"/> ("PDH", "DXGI budget", "sysfs", "nvidia-smi", "estimate", "none"), for the log.</param>
public sealed record VramReading(long TotalBytes, long FreeBytes, bool Measured, string Source)
{
    /// <summary>No graphics card (or its memory unknown).</summary>
    public static VramReading None { get; } = new(0, 0, false, "none");

    /// <summary>
    /// Free memory when nothing could measure it (the developer: Windows takes about an eighth): total × 7/8 − 0.5 GiB, never below 0.
    /// </summary>
    public static long Estimate(long totalBytes) => Math.Max(0, (totalBytes / 8 * 7) - (512L * 1024 * 1024));

    /// <summary>
    /// Free memory by the sources that count every process (Windows' performance counter, sysfs, nvidia-smi), when it is larger than
    /// <see cref="FreeBytes"/>: the smaller figure came from this app's own budget (DXGI), which Windows may lower while the window is in the
    /// background. A plan may be careful with <see cref="FreeBytes"/> but must not refuse a run on it alone. Null when no such source read more.
    /// </summary>
    public long? SystemFreeBytes { get; init; }

    /// <summary>The class of the card (<see cref="VramTiers.ForCard"/>).</summary>
    public VramTier Tier => VramTiers.ForCard(TotalBytes);
}