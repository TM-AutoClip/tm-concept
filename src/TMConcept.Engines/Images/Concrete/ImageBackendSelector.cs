using TMConcept.Core.Hardware;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Images;

/// <summary>
/// Which stable-diffusion.cpp build runs the local image models here, from the "Image acceleration" setting and the graphics cards found.
/// Automatic: an NVIDIA card whose CUDA driver loads → CUDA (Windows; Linux has no CUDA download, so Vulkan); otherwise any card with a
/// Vulkan loader → Vulkan; otherwise the processor. A chosen build that has no download for this computer (CUDA on Linux) falls back to the
/// automatic choice. The stable-diffusion.cpp component installs, and the image models wait for, exactly the chosen build's files (the CUDA
/// build with its CUDA runtime libraries): an AMD card got the CUDA build in 0.1.15 and sd-cli ended with 0xC0000135 (DLL missing).
/// </summary>
public sealed class ImageBackendSelector(IGpuInfo gpu, IImageAccelerationSetting setting, ToolInstaller tools)
{
    /// <summary>Every tool of every build (their folders are shown and removed together).</summary>
    public static readonly IReadOnlyList<ToolKind> AllTools =
        [ToolKind.ImageRuntime, ToolKind.ImageRuntimeCuda, ToolKind.ImageRuntimeVulkan, ToolKind.ImageRuntimeCpu];

    /// <summary>The build chosen now (the setting is read each time: a change applies to the next image).</summary>
    public ImageBackendChoice Choose() => Choose(setting.Value, gpu.Detect(), Available);

    /// <summary>What <see cref="ImageAcceleration.Automatic"/> chooses on this computer (shown next to the setting).</summary>
    public ImageBackendChoice Automatic() => Choose(ImageAcceleration.Automatic, gpu.Detect(), Available);

    /// <summary>Whether this computer has a download of every file of <paramref name="backend"/>.</summary>
    public bool Available(ImageBackend backend) => Tools(backend).All(t => tools.Archive(t) is not null);

    /// <summary>The tools of a build; the first one is sd-cli.</summary>
    public static IReadOnlyList<ToolKind> Tools(ImageBackend backend) => backend switch
    {
        ImageBackend.Cuda => [ToolKind.ImageRuntime, ToolKind.ImageRuntimeCuda],
        ImageBackend.Vulkan => [ToolKind.ImageRuntimeVulkan],
        _ => [ToolKind.ImageRuntimeCpu],
    };

    /// <summary>The choice for a setting on a computer (<paramref name="available"/>: whether a build has a download here).</summary>
    public static ImageBackendChoice Choose(ImageAcceleration setting, GpuReport report, Func<ImageBackend, bool> available)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(available);
        ImageBackend? wanted = setting switch
        {
            ImageAcceleration.Cuda => ImageBackend.Cuda,
            ImageAcceleration.Vulkan => ImageBackend.Vulkan,
            ImageAcceleration.Cpu => ImageBackend.Cpu,
            _ => null,
        };
        var backend = wanted is { } chosen && available(chosen) ? chosen : Automatic(report, available);
        var device = backend switch
        {
            ImageBackend.Cuda => report.Largest(GpuVendor.Nvidia),
            ImageBackend.Vulkan => report.Largest(),
            _ => null,
        };
        return new ImageBackendChoice(backend, setting, device);
    }

    private static ImageBackend Automatic(GpuReport report, Func<ImageBackend, bool> available)
    {
        if (report.CudaDriver && report.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia) && available(ImageBackend.Cuda))
        {
            return ImageBackend.Cuda;
        }

        return report.VulkanLoader && report.Adapters.Count > 0 && available(ImageBackend.Vulkan) ? ImageBackend.Vulkan : ImageBackend.Cpu;
    }
}
