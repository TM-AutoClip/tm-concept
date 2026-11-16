using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// Free graphics card memory now: every memory source of this computer (<see cref="IVideoMemorySource"/>; Windows: the performance counter
/// of the whole system and this process's DXGI budget, Linux: amdgpu sysfs and nvidia-smi) is asked, and the smallest figure wins (the AMD
/// Vulkan budget of one process can be well below "total − used"); the smallest figure of the sources that count every process is kept
/// beside it (<see cref="VramReading.SystemFreeBytes"/>), so this app's own budget alone never refuses a run. A card whose memory no source can read gets
/// <see cref="VramReading.Estimate"/> (total × 7/8 − 0.5 GiB). The card is the one asked for, or the one with the most memory. Each reading
/// is logged at debug level (sources and figures, no names).
/// </summary>
public sealed class VramProbe(IGpuInfo gpu, IEnumerable<IVideoMemorySource> sources, ILogger<VramProbe> logger) : IVramProbe
{
    private readonly IReadOnlyList<IVideoMemorySource> _sources = sources.ToList();

    /// <inheritdoc />
    public async Task<VramReading> MeasureAsync(GpuAdapter? adapter, CancellationToken cancellationToken)
    {
        var card = adapter ?? gpu.Detect().Largest();
        if (card?.VideoMemoryBytes is not { } total || total <= 0)
        {
            return VramReading.None;
        }

        long? smallest = null;
        long? system = null;
        var from = string.Empty;
        foreach (var source in _sources)
        {
            long? free;
            try
            {
                free = await source.FreeBytesAsync(card, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                // A source must not throw; one that does still leaves the others.
                logger.LogWarning(ex, "The free graphics card memory could not be read with {Source}", source.Name);
                continue;
            }

            if (free is not { } bytes)
            {
                continue;
            }

            bytes = Math.Clamp(bytes, 0, total);
            if (smallest is null || bytes < smallest)
            {
                smallest = bytes;
                from = source.Name;
            }

            if (!source.PerProcess && (system is null || bytes < system))
            {
                system = bytes;
            }
        }

        var reading = smallest is { } measured
            ? new VramReading(total, measured, true, from) { SystemFreeBytes = system > measured ? system : null }
            : new VramReading(total, VramReading.Estimate(total), false, "estimate");
        logger.LogDebug("Graphics card memory: {Free:0.00} of {Total:0.00} GiB free ({Source})", reading.FreeBytes / 1073741824.0, total / 1073741824.0,
            reading.Source);
        return reading;
    }
}
