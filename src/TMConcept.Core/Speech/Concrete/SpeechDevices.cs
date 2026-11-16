using TMConcept.Core.Hardware;

namespace TMConcept.Core.Speech;

/// <summary>
/// Where a local voice that runs on PyTorch can run: on an NVIDIA graphics card through CUDA (the card is there and the CUDA driver loads),
/// otherwise on the processor. PyTorch's Windows builds have no AMD or Intel graphics support, and its ROCm builds for Linux need AMD's own
/// runtime installed by hand, so an AMD or Intel card means the processor (slow for the big voices; the step says so).
/// </summary>
public static class SpeechDevices
{
    /// <summary>The NVIDIA card a CUDA build of PyTorch would use (the largest one); null when the voices run on the processor.</summary>
    public static GpuAdapter? CudaCard(GpuReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.CudaDriver ? report.Largest(GpuVendor.Nvidia) : null;
    }
}
