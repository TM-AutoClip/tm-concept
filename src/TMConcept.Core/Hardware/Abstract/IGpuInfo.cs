namespace TMConcept.Core.Hardware;

/// <summary>The graphics card memory of this computer, for the warning next to a local image model (never a block).</summary>
public interface IGpuInfo
{
    /// <summary>Memory of the largest graphics card in bytes; null when it cannot be read (no NVIDIA tool, another card).</summary>
    Task<long?> VideoMemoryBytesAsync(CancellationToken cancellationToken);
}
