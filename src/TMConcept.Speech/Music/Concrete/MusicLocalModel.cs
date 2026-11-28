using TMConcept.Core.Models;

namespace TMConcept.Speech.Music;

/// <summary>
/// A local music model as a model of the graphics card queue (0.1.46, C5; 28 Nov 2026 every local music model made for a card): on the card
/// when there is an NVIDIA one (its needs from the catalog: its card class and its main memory), otherwise on the processor. Each piece is a process of its own that ends with the piece,
/// so there is nothing to load or unload: the lease only keeps other local models off the card while it runs.
/// <para>0.1.59 (K24-a): the script's engine (exllamav2, PyTorch) keeps its weights on the card and the app cannot move them to main memory, so
/// "main memory, slower" is never planned. From three quarters of its class free (the class figure has headroom, as for a Python voice) it runs
/// on the card; below that the graphics driver would spill into shared memory (YuE 7B: 38 minutes without a line), so it is not started: the
/// memory question shows what is free and what it needs, with Try again and "Try anyway (may be very slow)".</para>
/// </summary>
public sealed class MusicLocalModel(CatalogModel catalog, bool nvidia) : LocalModelBase(catalog)
{
    /// <inheritdoc />
    public override ModelDevice Device => nvidia ? ModelDevice.Gpu : ModelDevice.Cpu;

    /// <summary>The script's engine keeps every weight on the card: never "main memory".</summary>
    protected override bool OffloadsToMainMemory => false;

    /// <inheritdoc />
    /// <remarks>The default plan; a refusal below the minimum becomes <see cref="MemoryPlacement.SharedMemory"/>: the user may still try it.</remarks>
    public override MemoryPlan PlanMemory(MemorySnapshot memory, ModelMemoryNeed? need)
    {
        var plan = base.PlanMemory(memory, need);
        return plan.Placement == MemoryPlacement.Refused ? plan with { Placement = MemoryPlacement.SharedMemory } : plan;
    }

    /// <summary>Three quarters of its class: below that it would not fit on the card with its working memory.</summary>
    protected override long MinimumCardBytes(ModelMemoryNeed need)
    {
        ArgumentNullException.ThrowIfNull(need);
        return need.VideoMemoryBytes / 4 * 3;
    }
}
