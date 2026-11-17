using System.Text.RegularExpressions;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.AppOllama;

/// <summary>
/// Which graphics backends of an unpacked Ollama package this computer keeps (0.1.20, the developer: download the official package, then
/// delete what this machine does not need). Ollama's backends are folders under <c>lib/ollama/</c>: <c>cuda_v12</c>, <c>cuda_v13</c>,
/// <c>vulkan</c> (and <c>rocm*</c> / <c>mlx*</c> in other packages); the processor libraries are files there and always stay. Kept:
/// <list type="bullet">
/// <item>an NVIDIA card whose CUDA driver loads: <c>cuda_v13</c> when the driver supports CUDA 13 (driver version 13000 or more) and the card
/// is a Turing or newer (compute capability 7.5+; CUDA 13 dropped older cards), otherwise <c>cuda_v12</c>; both when the driver's version
/// or the card's compute capability is not known (Ollama picks the one that loads);</item>
/// <item>a Vulkan loader with a card of a known maker (AMD, Intel, NVIDIA): <c>vulkan</c> (AMD and Intel run on it; for NVIDIA it is a small
/// fallback);</item>
/// <item>nothing else: Ollama runs on the processor.</item>
/// </list>
/// A folder whose name is not a known backend is never deleted.
/// </summary>
public static partial class AppOllamaBackends
{
    /// <summary>The CUDA 12 backend.</summary>
    public const string Cuda12 = "cuda_v12";

    /// <summary>The CUDA 13 backend.</summary>
    public const string Cuda13 = "cuda_v13";

    /// <summary>The Vulkan backend.</summary>
    public const string Vulkan = "vulkan";

    /// <summary>The backends to keep on a computer with <paramref name="report"/>.</summary>
    public static IReadOnlyList<string> Keep(GpuReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var keep = new List<string>();
        if (report.CudaDriver && report.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia))
        {
            var driver = report.Cuda.DriverVersion;
            var capability = report.Cuda.ComputeCapability;
            if (driver <= 0 || (driver >= 13000 && capability == 0))
            {
                // Without the driver's version, or without the card's capability under a CUDA 13 driver, the right one is not known.
                keep.Add(Cuda12);
                keep.Add(Cuda13);
            }
            else if (driver >= 13000 && capability >= 75)
            {
                keep.Add(Cuda13);
            }
            else
            {
                keep.Add(Cuda12);
            }
        }

        if (report.VulkanLoader && report.Adapters.Any(a => a.Vendor is GpuVendor.Nvidia or GpuVendor.Amd or GpuVendor.Intel))
        {
            keep.Add(Vulkan);
        }

        return keep;
    }

    /// <summary>Whether a folder of <c>lib/ollama/</c> is a graphics backend (only those are ever deleted).</summary>
    public static bool IsBackend(string folderName) => BackendName().IsMatch(folderName ?? string.Empty);

    /// <summary>
    /// The backend folders under <paramref name="libraryFolder"/> (<c>lib/ollama</c>) that this computer does not need (the caller deletes
    /// them). Empty when the folder does not exist.
    /// </summary>
    public static IReadOnlyList<string> Unneeded(string libraryFolder, GpuReport report)
    {
        ArgumentNullException.ThrowIfNull(libraryFolder);
        if (!Directory.Exists(libraryFolder))
        {
            return [];
        }

        var keep = Keep(report);
        return Directory.EnumerateDirectories(libraryFolder)
            .Where(d => IsBackend(Path.GetFileName(d)) && !keep.Contains(Path.GetFileName(d), StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The backend folder an archive entry lies in (<c>lib/ollama/cuda_v12/ggml-cuda.dll</c> → <c>cuda_v12</c>, with any folder above
    /// <c>lib</c>); null for an entry outside a backend folder (the program, the processor libraries).
    /// </summary>
    public static string? BackendOf(string entryName)
    {
        var parts = (entryName ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 2 < parts.Length; i++)
        {
            if (parts[i] == "lib" && parts[i + 1] == "ollama")
            {
                return IsBackend(parts[i + 2]) ? parts[i + 2] : null;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^(cuda_v\d+|vulkan|rocm[\w.]*|mlx[\w.]*)$")]
    private static partial Regex BackendName();
}
