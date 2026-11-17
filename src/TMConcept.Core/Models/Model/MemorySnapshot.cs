using TMConcept.Core.Hardware;

namespace TMConcept.Core.Models;

/// <summary>The memory measured at one moment: the graphics card's (<see cref="IVramProbe"/>) and the free main memory (<see cref="ISystemMemory"/>).</summary>
/// <param name="Card">The card's total and free memory (<see cref="VramReading.None"/> when there is no card or it is unknown).</param>
/// <param name="FreeMainBytes">Free main memory (null = unknown; unknown is never held against a model).</param>
public sealed record MemorySnapshot(VramReading Card, long? FreeMainBytes);
