using TMConcept.Core.Hardware;

namespace TMConcept.Tests.Desktop;

/// <summary>A fixed graphics card report (nothing native is loaded, no process is started).</summary>
internal sealed class FakeGpu(GpuReport report) : IGpuInfo
{
    /// <summary>One NVIDIA card of <paramref name="bytes"/> with its CUDA driver and Vulkan (<see cref="GpuReport.None"/>: no card at all).</summary>
    public FakeGpu(long bytes)
        : this(new GpuReport([new GpuAdapter(GpuVendor.Nvidia, "Test NVIDIA card", bytes)], true, true))
    {
    }

    public GpuReport Detect() => report;
}
