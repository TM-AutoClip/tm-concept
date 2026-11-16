namespace TMConcept.Engines.Hardware;

/// <summary>Whether the graphics compute runtimes of the drivers can be loaded (never throws).</summary>
public interface IComputeRuntimeProbe
{
    /// <summary>Whether the NVIDIA CUDA driver library loads (Windows: nvcuda.dll in System32; Linux: libcuda.so.1).</summary>
    bool CudaDriver();

    /// <summary>Whether the Vulkan loader loads (Windows: vulkan-1.dll in System32; Linux: libvulkan.so.1).</summary>
    bool VulkanLoader();

    /// <summary>
    /// The files of the Microsoft Visual C++ 2015–2022 runtime (x64) that do not load from System32 (Windows; always empty elsewhere).
    /// stable-diffusion.cpp's Windows builds (CUDA, Vulkan and processor alike) import them and do not ship them, so without them sd-cli
    /// ends at once with 0xC0000135 and prints nothing.
    /// </summary>
    IReadOnlyList<string> MissingVisualCppRuntime();
}
