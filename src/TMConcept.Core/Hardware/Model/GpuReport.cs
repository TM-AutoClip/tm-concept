namespace TMConcept.Core.Hardware;

/// <summary>What was found on this computer: its graphics cards and which compute runtimes can be loaded.</summary>
/// <param name="Adapters">The graphics cards (largest memory first is not guaranteed).</param>
/// <param name="CudaDriver">Whether the NVIDIA CUDA driver library loads (Windows nvcuda.dll, Linux libcuda.so.1).</param>
/// <param name="VulkanLoader">Whether the Vulkan loader loads (Windows vulkan-1.dll, Linux libvulkan.so.1).</param>
public sealed record GpuReport(IReadOnlyList<GpuAdapter> Adapters, bool CudaDriver, bool VulkanLoader)
{
    /// <summary>Nothing found (no card, no runtime).</summary>
    public static GpuReport None { get; } = new([], false, false);

    /// <summary>
    /// The card with the most own memory, of <paramref name="vendor"/> when given; a card whose memory is unknown comes after every card
    /// whose memory is known. Null when there is none.
    /// </summary>
    public GpuAdapter? Largest(GpuVendor? vendor = null) =>
        Adapters.Where(a => vendor is null || a.Vendor == vendor).OrderByDescending(a => a.VideoMemoryBytes ?? -1).FirstOrDefault();
}
