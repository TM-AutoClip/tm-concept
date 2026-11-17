using TMConcept.Core.Models;

namespace TMConcept.Engines.Images;

/// <summary>What <see cref="ImageMemoryPlanner"/> decided for one run: the level, sd-cli's memory options for it, what it needs and why it was refused.</summary>
/// <param name="Level">The level.</param>
/// <param name="Flags">sd-cli's memory options (flash attention is not here: the engine always passes it).</param>
/// <param name="ReserveGib">The graphics memory kept free for the system and other programs (<c>--max-vram -n</c>), in GiB.</param>
/// <param name="Reason">Why it was refused (empty otherwise).</param>
public sealed record ImageMemoryPlan(ImageMemoryLevel Level, IReadOnlyList<string> Flags, int ReserveGib, string Reason)
{
    /// <summary>Free card memory this level needs (with the reserve).</summary>
    public long NeededVideoBytes { get; init; }

    /// <summary>Free main memory this level needs (0 = none counted).</summary>
    public long NeededMainBytes { get; init; }

    /// <summary>The level as a placement of every local model (<see cref="MemoryPlacement"/>).</summary>
    public MemoryPlacement Placement => Level switch
    {
        ImageMemoryLevel.Card => MemoryPlacement.Card,
        ImageMemoryLevel.MainMemory => MemoryPlacement.MainMemory,
        ImageMemoryLevel.Disk or ImageMemoryLevel.DiskTextEncoder => MemoryPlacement.Disk,
        ImageMemoryLevel.Processor => MemoryPlacement.Processor,
        _ => MemoryPlacement.Refused,
    };

    /// <summary>The plan as every local model's <see cref="MemoryPlan"/>.</summary>
    public MemoryPlan ToMemoryPlan() => new(Placement, NeededVideoBytes, NeededMainBytes, Flags, Reason);
}
