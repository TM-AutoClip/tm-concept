namespace TMConcept.Core.Hardware;

/// <summary>
/// Free graphics card memory, read again at every call (unlike <see cref="IGpuInfo"/>, which looks once): taken before a local model is put
/// on the card (<see cref="Models.IGpuCoordinator"/>) and while waiting for an unloaded model's memory to come back. Windows: the performance
/// counter of the whole system (what Task Manager shows) and DXGI's budget of this process, the smaller of the two; Linux: the amdgpu sysfs
/// files, nvidia-smi for NVIDIA. Nothing measurable: <see cref="VramReading.Estimate"/>. Never throws.
/// </summary>
public interface IVramProbe
{
    /// <summary>
    /// The memory of <paramref name="adapter"/> (null = the card with the most memory, <see cref="GpuReport.Largest"/>);
    /// <see cref="VramReading.None"/> when there is no card or its memory is unknown.
    /// </summary>
    Task<VramReading> MeasureAsync(GpuAdapter? adapter, CancellationToken cancellationToken);
}