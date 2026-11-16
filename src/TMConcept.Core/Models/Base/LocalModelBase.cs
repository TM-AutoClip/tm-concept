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