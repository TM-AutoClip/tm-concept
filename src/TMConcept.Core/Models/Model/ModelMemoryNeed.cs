using TMConcept.Core.Hardware;

namespace TMConcept.Core.Models;

/// <summary>What a local model needs at one quality (estimates; the graphics card part includes its working memory).</summary>
/// <param name="Quality">The quality (<see cref="ModelQualities"/>).</param>
/// <param name="VideoMemoryBytes">Graphics card memory (0 = none: it runs on the processor).</param>
/// <param name="MemoryBytes">Main memory (RAM) besides, or instead when the weights are moved off the card (0 = unknown or small).</param>
public sealed record ModelMemoryNeed(string Quality, long VideoMemoryBytes, long MemoryBytes)
{
    private const double Gibibyte = 1024.0 * 1024 * 1024;

    /// <summary>The graphics card class this quality needs.</summary>
    public VramTier Tier => VramTiers.ForModel(Math.Ceiling(VideoMemoryBytes / Gibibyte * 10) / 10);

    /// <summary>A need in whole GB (the catalog's <see cref="CatalogModel.MinVideoMemoryGb"/> and <see cref="CatalogModel.MemoryGb"/>).</summary>
    public static ModelMemoryNeed FromGigabytes(string quality, double videoGigabytes, double memoryGigabytes) =>
        new(quality, (long)(Math.Max(0, videoGigabytes) * Gibibyte), (long)(Math.Max(0, memoryGigabytes) * Gibibyte));

    /// <summary>
    /// The best quality of <paramref name="needs"/> (lowest first) whose graphics memory fits in <paramref name="freeBytes"/>; null when none
    /// fits (the caller moves weights to main memory or runs on the processor).
    /// </summary>
    public static ModelMemoryNeed? Best(IReadOnlyList<ModelMemoryNeed> needs, long freeBytes)
    {
        ArgumentNullException.ThrowIfNull(needs);
        return needs.LastOrDefault(n => n.VideoMemoryBytes <= freeBytes);
    }
}