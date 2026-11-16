using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// One way of reading how much of a graphics card's own memory is free now (Windows: the performance counter and DXGI's budget; Linux: amdgpu
/// sysfs and nvidia-smi). <see cref="VramProbe"/> asks every source of this computer and takes the smallest figure. Never throws.
/// </summary>
public interface IVideoMemorySource
{
    /// <summary>A short name for the log ("PDH", "DXGI budget", "sysfs", "nvidia-smi").</summary>
    string Name { get; }

    /// <summary>Free bytes of <paramref name="adapter"/>'s own memory now; null when this source cannot read that card.</summary>
    Task<long?> FreeBytesAsync(GpuAdapter adapter, CancellationToken cancellationToken);
}
