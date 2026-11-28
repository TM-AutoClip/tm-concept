namespace TMConcept.Core.Models;

/// <summary>
/// Where a local model goes with the memory of a <see cref="MemorySnapshot"/> (<see cref="ILocalModel.PlanMemory"/>): the placement, what it
/// needs at that placement, the engine's own options for it, and why it was refused.
/// </summary>
/// <param name="Placement">The placement.</param>
/// <param name="NeededVideoBytes">Graphics card memory this placement needs free (weights on the card and working memory; 0 = none).</param>
/// <param name="NeededMainBytes">Main memory this placement needs free (0 = none or unknown).</param>
/// <param name="Arguments">The engine's options for this placement (sd-cli's memory flags; empty for a model that places itself).</param>
/// <param name="Reason">Why it was refused (empty otherwise), in English for the log.</param>
public sealed record MemoryPlan(MemoryPlacement Placement, long NeededVideoBytes, long NeededMainBytes, IReadOnlyList<string> Arguments, string Reason)
{
    /// <summary>
    /// Whether the model can run with this plan without asking: not <see cref="MemoryPlacement.Refused"/> and not
    /// <see cref="MemoryPlacement.SharedMemory"/> (that one waits for the user's "Try anyway").
    /// </summary>
    public bool Runs => Placement is not (MemoryPlacement.Refused or MemoryPlacement.SharedMemory);

    /// <summary>Whether the user may still start it, knowing it will be very slow (<see cref="MemoryPlacement.SharedMemory"/>).</summary>
    public bool CanTryAnyway => Placement == MemoryPlacement.SharedMemory;

    /// <summary>Whether the model runs but not wholly on the card (main memory, disk, or shared memory after "Try anyway"): slower, shown as a warning.</summary>
    public bool Slower => Placement is MemoryPlacement.MainMemory or MemoryPlacement.Disk or MemoryPlacement.SharedMemory;
}
