using TMConcept.Core.Hardware;

namespace TMConcept.Core.Speech;

/// <summary>
/// Where a local voice that runs on PyTorch can run: on an NVIDIA graphics card through CUDA (the card is there, the CUDA driver loads, and
/// it is new enough for the PyTorch CUDA 12.8 build: driver for CUDA 12.8 (570 or newer) and a card of compute capability 7.0 or higher,
/// GTX 16 / RTX 20 and later; PyTorch 2.8's CUDA 12.8 build has no kernels for the GTX 10 and older cards), otherwise on the processor.
/// What the driver could not tell is not held against the card. PyTorch's Windows builds have no AMD or Intel graphics support, and its ROCm
/// builds for Linux need AMD's own runtime installed by hand, so an AMD or Intel card means the processor (slow for the big voices; the step
/// says so).
/// </summary>
public static class SpeechDevices
{
    /// <summary>The smallest CUDA driver version the PyTorch CUDA 12.8 build runs on (12.8: NVIDIA driver 570 or newer).</summary>
    public const int MinimumCudaDriver = 12080;

    /// <summary>The smallest compute capability (× 10) the PyTorch 2.8 CUDA 12.8 build has kernels for (Volta 7.0; Pascal 6.x is gone).</summary>
    public const int MinimumComputeCapability = 70;

    /// <summary>The NVIDIA card a CUDA build of PyTorch would use (the largest one); null when the voices run on the processor.</summary>
    public static GpuAdapter? CudaCard(GpuReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!report.CudaDriver || !Supported(report.Cuda))
        {
            return null;
        }

        return report.Largest(GpuVendor.Nvidia);
    }

    /// <summary>Whether the driver and the card are new enough for the PyTorch CUDA build (an unknown figure counts as enough).</summary>
    public static bool Supported(CudaDetails cuda)
    {
        ArgumentNullException.ThrowIfNull(cuda);
        return (cuda.DriverVersion == 0 || cuda.DriverVersion >= MinimumCudaDriver)
            && (cuda.ComputeCapability == 0 || cuda.ComputeCapability >= MinimumComputeCapability);
    }
}
