using System.Runtime.InteropServices;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// Loads the drivers' compute libraries once and frees them at once: on Windows by full path from System32 (no DLL planting from the working
/// folder), on Linux by soname through the loader's search. Loading proves the driver is installed, not that a card works (CUDA without a
/// supported card still loads); the run of sd-cli then says more.
/// </summary>
public sealed class NativeRuntimeProbe : IComputeRuntimeProbe
{
    /// <inheritdoc />
    public bool CudaDriver() => Loads(OperatingSystem.IsWindows() ? System32("nvcuda.dll") : "libcuda.so.1");

    /// <inheritdoc />
    public bool VulkanLoader() => Loads(OperatingSystem.IsWindows() ? System32("vulkan-1.dll") : "libvulkan.so.1");

    private static string System32(string file) => Path.Combine(Environment.SystemDirectory, file);

    private static bool Loads(string library)
    {
        try
        {
            if (!NativeLibrary.TryLoad(library, out var handle))
            {
                return false;
            }

            NativeLibrary.Free(handle);
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException or DllNotFoundException or ArgumentException)
        {
            return false;
        }
    }
}
