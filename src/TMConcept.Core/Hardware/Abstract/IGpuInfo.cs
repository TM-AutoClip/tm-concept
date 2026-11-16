namespace TMConcept.Core.Hardware;

/// <summary>
/// The graphics cards of this computer and the compute runtimes that load (CUDA driver, Vulkan loader): chooses the image build and the
/// memory flags, and drives the memory note next to a local image model. Looked at once per run (cached); never a block by itself.
/// </summary>
public interface IGpuInfo
{
    /// <summary>What was found (cached after the first call; native calls and file reads, at most one short nvidia-smi run on Linux).</summary>
    GpuReport Detect();
}
