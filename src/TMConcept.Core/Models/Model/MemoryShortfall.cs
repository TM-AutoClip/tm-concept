namespace TMConcept.Core.Models;

/// <summary>
/// The figures shown when a local model's memory is not free (<see cref="IMemoryShortfallHandler"/>): the card's total and free memory with
/// the source of the reading, what the model needs, and the free main memory.
/// </summary>
/// <param name="ModelName">The model's display name.</param>
/// <param name="CardTotalBytes">The card's own memory (0 = unknown).</param>
/// <param name="CardFreeBytes">Free on the card now.</param>
/// <param name="Source">Which source measured it (PDH, DXGI budget, sysfs, nvidia-smi…).</param>
/// <param name="NeededVideoBytes">What the model needs free on the card at its lowest level.</param>
/// <param name="FreeMainBytes">Free main memory now (null = unknown).</param>
/// <param name="NeededMainBytes">Main memory it needs at that level (0 = none or unknown).</param>
public sealed record MemoryShortfall(string ModelName, long CardTotalBytes, long CardFreeBytes, string Source, long NeededVideoBytes, long? FreeMainBytes,
    long NeededMainBytes)
{
    /// <summary>
    /// TM Concept's own local models whose unload failed and that may still hold the card's memory (their names; empty = none): the question
    /// says so, since closing games and browsers would not help then.
    /// </summary>
    public IReadOnlyList<string> HeldBy { get; init; } = [];
}
