namespace TMConcept.Engines.Hardware;

/// <summary>Whether the graphics compute runtimes of the drivers can be loaded (never throws).</summary>
public interface IComputeRuntimeProbe
{
    /// <summary>Whether the NVIDIA CUDA driver library loads (Windows: nvcuda.dll in System32; Linux: libcuda.so.1).</summary>
    bool CudaDriver();

    /// <summary>Whether the Vulkan loader loads (Windows: vulkan-1.dll in System32; Linux: libvulkan.so.1).</summary>
    bool VulkanLoader();
}
