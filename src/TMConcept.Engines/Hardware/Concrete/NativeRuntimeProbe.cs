using System.Runtime.InteropServices;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// Loads the drivers' compute libraries once and frees them at once: on Windows by full path from System32 (no DLL planting from the working
/// folder), on Linux by soname through the loader's search. Loading proves the driver is installed, not that a card works (CUDA without a
/// supported card still loads); the run of sd-cli then says more.
/// </summary>
public sealed class NativeRuntimeProbe : IComputeRuntimeProbe
{
    /// <summary>
    /// The Visual C++ runtime files sd-cli needs on Windows: sd-cli.exe, stable-diffusion.dll and ggml-vulkan.dll import MSVCP140,
    /// VCRUNTIME140 and VCRUNTIME140_1, the ggml-cpu-*.dll files also VCOMP140 (OpenMP); none of the zips carries them.
    /// </summary>
    public static readonly IReadOnlyList<string> VisualCppRuntimeFiles = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcomp140.dll"];

    /// <inheritdoc />
    public bool CudaDriver() => Loads(OperatingSystem.IsWindows() ? System32("nvcuda.dll") : "libcuda.so.1");

    /// <inheritdoc />
    public bool VulkanLoader() => Loads(OperatingSystem.IsWindows() ? System32("vulkan-1.dll") : "libvulkan.so.1");

    /// <inheritdoc />
    public IReadOnlyList<string> MissingVisualCppRuntime() =>
        OperatingSystem.IsWindows() ? VisualCppRuntimeFiles.Where(f => !Loads(System32(f))).ToList() : [];

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
