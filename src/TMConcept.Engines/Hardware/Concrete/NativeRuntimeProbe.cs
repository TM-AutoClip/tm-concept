using System.Runtime.InteropServices;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// Loads the drivers' compute libraries once and frees them at once: on Windows by full path from System32 (no DLL planting from the working
/// folder), on Linux by soname through the loader's search. Loading proves the driver is installed, not that a card works (CUDA without a
/// supported card still loads); the run of sd-cli then says more.
/// </summary>
public sealed class NativeRuntimeProbe : IComputeRuntimeProbe
{
    private const int CudaSuccess = 0;
    private const int ComputeCapabilityMajor = 75;
    private const int ComputeCapabilityMinor = 76;

    /// <summary>
    /// The Visual C++ runtime files sd-cli needs on Windows: sd-cli.exe, stable-diffusion.dll and ggml-vulkan.dll import MSVCP140,
    /// VCRUNTIME140 and VCRUNTIME140_1, the ggml-cpu-*.dll files also VCOMP140 (OpenMP), and since master-1106 sd-cli.exe and
    /// stable-diffusion.dll also MSVCP140_CODECVT_IDS (all part of the same redistributable); none of the zips carries them.
    /// </summary>
    public static readonly IReadOnlyList<string> VisualCppRuntimeFiles =
        ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcomp140.dll", "msvcp140_codecvt_ids.dll"];

    /// <inheritdoc />
    public bool CudaDriver() => Loads(OperatingSystem.IsWindows() ? System32("nvcuda.dll") : "libcuda.so.1");

    /// <inheritdoc />
    public CudaDetails Cuda()
    {
        try
        {
            return AskCuda(OperatingSystem.IsWindows() ? System32("nvcuda.dll") : "libcuda.so.1");
        }
        catch (Exception ex) when (ex is BadImageFormatException or DllNotFoundException or ArgumentException or EntryPointNotFoundException)
        {
            return CudaDetails.Unknown;
        }
    }

    /// <inheritdoc />
    public bool VulkanLoader() => Loads(OperatingSystem.IsWindows() ? System32("vulkan-1.dll") : "libvulkan.so.1");

    /// <inheritdoc />
    public IReadOnlyList<string> MissingVisualCppRuntime() =>
        OperatingSystem.IsWindows() ? VisualCppRuntimeFiles.Where(f => !Loads(System32(f))).ToList() : [];

    private static string System32(string file) => Path.Combine(Environment.SystemDirectory, file);

    /// <summary>
    /// The driver API itself (once per run, through the detector's cache): <c>cuDriverGetVersion</c>, then <c>cuInit</c> and every card's
    /// compute capability. After <c>cuInit</c> the library stays loaded (the driver may keep threads of its own); it creates no context and
    /// takes no graphics memory.
    /// </summary>
    private static unsafe CudaDetails AskCuda(string library)
    {
        if (!NativeLibrary.TryLoad(library, out var handle))
        {
            return CudaDetails.Unknown;
        }

        if (!NativeLibrary.TryGetExport(handle, "cuDriverGetVersion", out var getVersion))
        {
            NativeLibrary.Free(handle);
            return CudaDetails.Unknown;
        }

        int version;
        if (((delegate* unmanaged<int*, int>)getVersion)(&version) != CudaSuccess)
        {
            version = 0;
        }

        if (!NativeLibrary.TryGetExport(handle, "cuInit", out var init) || !NativeLibrary.TryGetExport(handle, "cuDeviceGetCount", out var getCount)
            || !NativeLibrary.TryGetExport(handle, "cuDeviceGet", out var getDevice) || !NativeLibrary.TryGetExport(handle, "cuDeviceGetAttribute", out var getAttribute)
            || ((delegate* unmanaged<uint, int>)init)(0) != CudaSuccess)
        {
            return new CudaDetails(version, 0);
        }

        int count;
        if (((delegate* unmanaged<int*, int>)getCount)(&count) != CudaSuccess)
        {
            return new CudaDetails(version, 0);
        }

        var best = 0;
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            int device, major, minor;
            if (((delegate* unmanaged<int*, int, int>)getDevice)(&device, ordinal) == CudaSuccess
                && ((delegate* unmanaged<int*, int, int, int>)getAttribute)(&major, ComputeCapabilityMajor, device) == CudaSuccess
                && ((delegate* unmanaged<int*, int, int, int>)getAttribute)(&minor, ComputeCapabilityMinor, device) == CudaSuccess)
            {
                best = Math.Max(best, (major * 10) + minor);
            }
        }

        return new CudaDetails(version, best);
    }

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
