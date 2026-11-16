using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// The graphics cards of this computer from the operating system's sources, in order (Windows: DXGI, then the registry; Linux: sysfs, then
/// nvidia-smi): the first source that lists a card gives the list; a later source is asked only when a card's memory is still unknown and
/// fills it in (the registry's 64-bit memory value, nvidia-smi for an NVIDIA card on Linux, whose sysfs has no memory file). Then whether the
/// CUDA driver and the Vulkan loader load. Looked at once per run; one Information line says what was found (names and sizes, no serials).
/// Never WMI: its memory value is 32-bit (4 GB at most) and it needs built-in COM, which a trimmed app does not have.
/// </summary>
public sealed class GpuDetector(IEnumerable<IGraphicsAdapterSource> sources, IComputeRuntimeProbe runtimes, ILogger<GpuDetector> logger) : IGpuInfo
{
    private readonly Lazy<GpuReport> _report = new(() => Find(sources, runtimes, logger), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <inheritdoc />
    public GpuReport Detect() => _report.Value;

    private static GpuReport Find(IEnumerable<IGraphicsAdapterSource> sources, IComputeRuntimeProbe runtimes, ILogger logger)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<GpuAdapter> adapters = [];
        foreach (var source in sources)
        {
            if (adapters.Count > 0 && adapters.All(a => a.VideoMemoryBytes is not null))
            {
                break;
            }

            IReadOnlyList<GpuAdapter> found;
            try
            {
                found = source.Read();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A source must not throw; one that does still leaves the others (a native probe must never stop the app).
                logger.LogWarning(ex, "Graphics cards could not be read with {Source}", source.Name);
                continue;
            }

            adapters = Merge(adapters, found);
        }

        bool cuda;
        bool vulkan;
        try
        {
            cuda = runtimes.CudaDriver();
            vulkan = runtimes.VulkanLoader();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "The graphics runtimes could not be checked");
            cuda = false;
            vulkan = false;
        }

        var details = CudaDetails.Unknown;
        if (cuda && adapters.Any(a => a.Vendor == GpuVendor.Nvidia))
        {
            try
            {
                details = runtimes.Cuda();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger.LogWarning(ex, "The CUDA driver could not be asked for its version");
            }
        }

        var report = new GpuReport(adapters, cuda, vulkan) { Cuda = details };
        logger.LogInformation("Graphics cards: {Cards}; CUDA driver {Cuda} (CUDA {Version}, compute capability {Compute}), Vulkan loader {Vulkan} ({Milliseconds} ms)",
            Describe(report), cuda ? "found" : "not found", details.DriverVersion, details.ComputeCapability, vulkan ? "found" : "not found",
            watch.ElapsedMilliseconds);
        return report;
    }

    /// <summary>
    /// The cards of <paramref name="known"/> with what <paramref name="found"/> adds: all of <paramref name="found"/> when nothing is known
    /// yet; otherwise a card whose memory is unknown takes the memory (and the fuller name) of the matching card of the same maker: the one
    /// with the same name, or the only one of that maker on both sides.
    /// </summary>
    public static IReadOnlyList<GpuAdapter> Merge(IReadOnlyList<GpuAdapter> known, IReadOnlyList<GpuAdapter> found)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(found);
        if (known.Count == 0)
        {
            return found;
        }

        return known.Select(card =>
        {
            if (card.VideoMemoryBytes is not null)
            {
                return card;
            }

            var sameMaker = found.Where(f => f.Vendor == card.Vendor && f.VideoMemoryBytes is not null).ToList();
            var match = sameMaker.FirstOrDefault(f => string.Equals(f.Name, card.Name, StringComparison.OrdinalIgnoreCase))
                ?? (sameMaker.Count == 1 && known.Count(k => k.Vendor == card.Vendor) == 1 ? sameMaker[0] : null);
            return match is null ? card : card with { Name = match.Name.Length > 0 ? match.Name : card.Name, VideoMemoryBytes = match.VideoMemoryBytes };
        }).ToList();
    }

    /// <summary>"AMD Radeon RX 7800 XT 16 GB, Intel UHD Graphics 0.1 GB" or "none".</summary>
    public static string Describe(GpuReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Adapters.Count == 0
            ? "none"
            : string.Join(", ", report.Adapters.Select(a => a.Name + " " + (a.VideoMemoryBytes is { } bytes
                ? (bytes / (1024.0 * 1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " GB"
                : "memory unknown")));
    }
}
