using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>Whether the graphics compute runtimes of the drivers can be loaded (never throws).</summary>
public interface IComputeRuntimeProbe
{
    /// <summary>Whether the NVIDIA CUDA driver library loads (Windows: nvcuda.dll in System32; Linux: libcuda.so.1).</summary>
    bool CudaDriver();

    /// <summary>
    /// The CUDA driver's version and the highest compute capability of its cards (asked of the driver library itself, no process is started);
    /// <see cref="CudaDetails.Unknown"/> when the driver is missing or cannot be asked.
    /// </summary>
    CudaDetails Cuda() => CudaDetails.Unknown;

    /// <summary>Whether the Vulkan loader loads (Windows: vulkan-1.dll in System32; Linux: libvulkan.so.1).</summary>
    bool VulkanLoader();

    /// <summary>
    /// The files of the Microsoft Visual C++ 2015–2022 runtime (x64) that do not load from System32 (Windows; always empty elsewhere).
    /// stable-diffusion.cpp's Windows builds (CUDA, Vulkan and processor alike) import them and do not ship them, so without them sd-cli
    /// ends at once with 0xC0000135 and prints nothing.
    /// </summary>
    IReadOnlyList<string> MissingVisualCppRuntime();
}
