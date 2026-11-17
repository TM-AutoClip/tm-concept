using TMConcept.Core.Hardware;

namespace TMConcept.Core.Models;

/// <summary>
/// The base of a local model: keeps <see cref="IsLoaded"/> and runs one load or unload at a time. A subclass says where it runs
/// (<see cref="Device"/>) and overrides <see cref="OnLoadAsync"/> / <see cref="OnUnloadAsync"/> when there is something to do (both do nothing
/// here). <see cref="Needs"/> defaults to the catalog's <see cref="CatalogModel.MinVideoMemoryGb"/> and <see cref="CatalogModel.MemoryGb"/>.
/// </summary>
public abstract class LocalModelBase(CatalogModel catalog) : ModelBase(catalog), ILocalModel
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _loaded;

    /// <inheritdoc />
    public sealed override bool IsLocal => true;

    /// <inheritdoc />
    public abstract ModelDevice Device { get; }

    /// <inheritdoc />
    public virtual IReadOnlyList<ModelMemoryNeed> Needs =>
        [ModelMemoryNeed.FromGigabytes(ModelQualities.Standard, Device == ModelDevice.Gpu ? Catalog.MinVideoMemoryGb : 0, Catalog.MemoryGb)];

    /// <summary>
    /// The least graphics card memory it must find free to run at all, with its weights wherever they fit (0 = it never refuses: a server
    /// such as Ollama moves what does not fit to main memory or the processor by itself). A model that cannot leave weights off the card
    /// (a PyTorch voice) overrides it.
    /// </summary>
    protected virtual long MinimumCardBytes(ModelMemoryNeed need) => 0;

    /// <summary>
    /// Whether what does not fit on the card really goes to main memory (true: a server such as Ollama or LM Studio places the rest itself).
    /// False for a model that keeps every weight on the card (a PyTorch voice): above <see cref="MinimumCardBytes"/> it runs on the card (its
    /// class figure has headroom), below it is refused; it is never planned in main memory, which would be a false "slower, RAM is used".
    /// </summary>
    protected virtual bool OffloadsToMainMemory => true;

    /// <inheritdoc />
    /// <remarks>
    /// The default: the processor when it does not run on the card; on the card when its graphics memory need fits in the free memory (the
    /// larger of this app's budget and what is free across all processes) or the memory is not measured; otherwise main memory (slower,
    /// the server places what does not fit), refused only below <see cref="MinimumCardBytes"/>. A model that does not offload
    /// (<see cref="OffloadsToMainMemory"/>) runs on the card from its minimum up.
    /// </remarks>
    public virtual MemoryPlan PlanMemory(MemorySnapshot memory, ModelMemoryNeed? need)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var wanted = need ?? Needs.FirstOrDefault() ?? new ModelMemoryNeed(ModelQualities.Standard, 0, 0);
        if (Device != ModelDevice.Gpu)
        {
            return new MemoryPlan(MemoryPlacement.Processor, 0, wanted.MemoryBytes, [], string.Empty);
        }

        var card = memory.Card;
        var free = Math.Max(card.FreeBytes, card.SystemFreeBytes ?? 0);
        if (!card.Measured || card.TotalBytes <= 0 || wanted.VideoMemoryBytes <= free)
        {
            return new MemoryPlan(MemoryPlacement.Card, wanted.VideoMemoryBytes, wanted.MemoryBytes, [], string.Empty);
        }

        var minimum = MinimumCardBytes(wanted);
        if (free < minimum)
        {
            return new MemoryPlan(MemoryPlacement.Refused, minimum, wanted.MemoryBytes, [],
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Name} needs {minimum / (1024.0 * 1024 * 1024):0.0} GiB of free graphics memory; {free / (1024.0 * 1024 * 1024):0.0} GiB are free."));
        }

        return OffloadsToMainMemory
            ? new MemoryPlan(MemoryPlacement.MainMemory, minimum, wanted.MemoryBytes + (wanted.VideoMemoryBytes - free), [], string.Empty)
            : new MemoryPlan(MemoryPlacement.Card, minimum, wanted.MemoryBytes, [], string.Empty);
    }

    /// <inheritdoc />
    public VramTier Tier => Device == ModelDevice.Gpu ? Needs.Select(n => n.Tier).DefaultIfEmpty(VramTier.None).Min() : VramTier.None;

    /// <inheritdoc />
    public bool IsLoaded => _loaded;

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_loaded)
            {
                await OnLoadAsync(cancellationToken).ConfigureAwait(false);
                _loaded = true;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task UnloadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await OnUnloadAsync(cancellationToken).ConfigureAwait(false);
            _loaded = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Loads (nothing here: the model loads on its first request).</summary>
    protected virtual Task OnLoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Frees the model (nothing here: its process ends after every request).</summary>
    protected virtual Task OnUnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}