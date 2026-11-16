namespace TMConcept.Core.Models;

/// <summary>Where a local model runs.</summary>
public enum ModelDevice
{
    /// <summary>On the graphics card: one such model at a time (<see cref="IGpuCoordinator"/>).</summary>
    Gpu = 1,

    /// <summary>On the processor and main memory only: never waits for the graphics card.</summary>
    Cpu = 2,
}