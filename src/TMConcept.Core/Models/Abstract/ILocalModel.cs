using TMConcept.Core.Hardware;

namespace TMConcept.Core.Models;

/// <summary>
/// A model that runs on this computer: a local image model (sd-cli), a local voice or the local embedder (the speech worker), a model of a
/// server on this computer (Ollama, LM Studio, llama-server…). A model on the graphics card (<see cref="Device"/> GPU) is used only inside a
/// lease of <see cref="IGpuCoordinator"/>: the coordinator unloads the model that was on the card before (<see cref="UnloadAsync"/>), waits
/// until its memory is free again, then <see cref="LoadAsync"/> this one. Implement it on <see cref="LocalModelBase"/>, give it out from an
/// <see cref="ILocalModelSource"/> and register the source.
/// </summary>
public interface ILocalModel : IModel
{
    /// <summary>Where it runs now (it may change with a setting: the processor build of sd-cli runs on the CPU).</summary>
    ModelDevice Device { get; }

    /// <summary>
    /// What it needs per quality (graphics card and main memory), lowest quality first; at least one entry. The coordinator waits for the
    /// first one's graphics memory when the caller names none.
    /// </summary>
    IReadOnlyList<ModelMemoryNeed> Needs { get; }

    /// <summary>The smallest graphics card class it runs in (<see cref="VramTiers"/>; none = no card needed).</summary>
    VramTier Tier { get; }

    /// <summary>
    /// Where its weights go with the memory of <paramref name="memory"/> (0.1.18: every local model's own memory behaviour, overridable per
    /// card class and measured memory: <see cref="LocalModelBase.PlanMemory"/> has the defaults, an image model family its own table). Never
    /// another model: only the placement (card, main memory, disk) changes. <see cref="MemoryPlacement.Refused"/> only when even the slowest
    /// placement cannot run; <see cref="IMemoryWaiter"/> then waits for memory and asks the user.
    /// </summary>
    /// <param name="memory">What is free now.</param>
    /// <param name="need">The quality asked for (null = the first of <see cref="Needs"/>).</param>
    MemoryPlan PlanMemory(MemorySnapshot memory, ModelMemoryNeed? need);

    /// <summary>Whether it is loaded now (or may be: a server that loads on the first request counts as loaded after its lease was granted).</summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Makes it ready before its first use in a lease (may do nothing: sd-cli and a server load on the first request). Safe to call again.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Frees what it holds (graphics card and main memory): stops its process, asks its server to unload it. Safe to call when it is not
    /// loaded. Throws <see cref="ModelUnloadException"/> when the unload cannot be confirmed (the model may still hold memory): it then still
    /// counts as loaded, and <see cref="IGpuCoordinator"/> asks again later.
    /// </summary>
    Task UnloadAsync(CancellationToken cancellationToken);
}