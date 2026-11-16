using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// One way of listing the graphics cards (DXGI or the registry on Windows; sysfs or nvidia-smi on Linux). Never throws: what cannot be read
/// gives an empty list or a card without memory, and the next source may fill it in.
/// </summary>
public interface IGraphicsAdapterSource
{
    /// <summary>A short name for the log ("DXGI", "registry", "sysfs", "nvidia-smi").</summary>
    string Name { get; }

    /// <summary>The cards this source sees (software adapters left out).</summary>
    IReadOnlyList<GpuAdapter> Read();
}
