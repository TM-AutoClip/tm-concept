using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;

namespace TMConcept.Core.Models;

/// <summary>
/// The app's one graphics card queue (see <see cref="IGpuCoordinator"/>). The model on the card stays after its lease ends (the next batch of
/// the same model needs no reload); it is unloaded only when another local model asks for the card, or by <see cref="FreeAsync"/>. Before the
/// first card lease of a run, every <see cref="IResidentModelSweep"/> frees what local servers still hold from before. A model whose unload
/// could not be confirmed (<see cref="ModelUnloadException"/>, or any other failure) is not forgotten: it is asked again at every later change
/// of model and by <see cref="FreeAsync"/>; meanwhile the next model gets the card with the memory measured after the failed unload (sd-cli
/// and the voices plan from that reading). Waiting leases are not ordered. Each change is logged (model codes and memory figures only).
/// </summary>
public sealed class GpuCoordinator(IModelDirectory directory, IVramProbe probe, TimeProvider clock, GpuCoordinatorOptions options, ILogger<GpuCoordinator> logger,
    IEnumerable<IResidentModelSweep>? sweeps = null)
    : IGpuCoordinator
{
    private const double Gibibyte = 1024.0 * 1024 * 1024;

    // The keys of the card leases the current flow holds (a flow that asks for a second model while it holds one would wait for itself).
    private static readonly AsyncLocal<List<string>?> Held = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IReadOnlyList<IResidentModelSweep> _sweeps = sweeps?.ToList() ?? [];

    // Models whose unload failed: they may still hold memory, so they are asked again later (never the one about to be used).
    private readonly List<ILocalModel> _stuck = [];
    private ILocalModel? _loaded;
    private int _users;
    private bool _swept;
    private TaskCompletionSource _idle = Completed();

    /// <inheritdoc />
    public ILocalModel? Loaded => _loaded;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public Task<IGpuLease> AcquireAsync(CatalogModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        return AcquireAsync(directory.Get(model), null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IGpuLease> AcquireAsync(IModel model, ModelMemoryNeed? need, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not ILocalModel { Device: ModelDevice.Gpu } local)
        {
            return Task.FromResult<IGpuLease>(new GpuLease(model, false, VramReading.None, null));
        }

        // Set here, outside the async part, so the list belongs to the caller's flow (an async method's own changes are undone when it returns).
        var held = Held.Value ??= [];
        lock (held)
        {
            if (held.FirstOrDefault(k => k != local.Key) is { } other)
            {
                throw new InvalidOperationException($"The graphics card is already leased for {other} in this flow; release it before asking for {local.Key}.");
            }
        }

        return GrantAsync(local, need ?? local.Needs.FirstOrDefault(), held, cancellationToken);
    }

    /// <inheritdoc />
    public async Task FreeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_users > 0)
            {
                return;
            }

            var loaded = _loaded;
            if (loaded is not null && await UnloadAsync(loaded, cancellationToken).ConfigureAwait(false))
            {
                logger.LogInformation("Graphics card freed: {Model} unloaded", loaded.Key);
            }

            await RetryStuckAsync(null, loaded, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IGpuLease> GrantAsync(ILocalModel model, ModelMemoryNeed? need, List<string> held, CancellationToken cancellationToken)
    {
        var waited = false;
        while (true)
        {
            Task? busy = null;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_loaded is not null && _loaded.Key != model.Key)
                {
                    if (_users > 0)
                    {
                        if (!waited)
                        {
                            logger.LogInformation("{Model} waits for the graphics card: {Other} is in use", model.Key, _loaded.Key);
                            waited = true;
                        }

                        busy = _idle.Task;
                    }
                    else
                    {
                        var previous = _loaded;
                        await UnloadAsync(previous, cancellationToken).ConfigureAwait(false);
                        await RetryStuckAsync(model, previous, cancellationToken).ConfigureAwait(false);
                        var freed = await WaitForMemoryAsync(need, cancellationToken).ConfigureAwait(false);
                        logger.LogInformation("Graphics card: {Previous} unloaded for {Model}; {Free:0.0} of {Total:0.0} GiB free ({Source})", previous.Key, model.Key,
                            freed.FreeBytes / Gibibyte, freed.TotalBytes / Gibibyte, freed.Source);
                    }
                }
                else if (_loaded is null)
                {
                    await SweepOnceAsync(model, cancellationToken).ConfigureAwait(false);
                    await RetryStuckAsync(model, null, cancellationToken).ConfigureAwait(false);
                }

                if (busy is null)
                {
                    return await GrantNowAsync(model, held, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }

            await busy.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Loads <paramref name="model"/>, reads the memory it may use and hands out the lease (called with the gate held). The user count goes up
    /// only after the last step that can fail or be cancelled: a Stop during the load or the reading leaves no count behind (a count without a
    /// lease would keep every other local model waiting until the app is restarted).
    /// </summary>
    private async Task<IGpuLease> GrantNowAsync(ILocalModel model, List<string> held, CancellationToken cancellationToken)
    {
        var changed = _loaded is null;
        if (!model.IsLoaded)
        {
            await model.LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        _loaded = model;
        _stuck.Remove(model);
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        var memory = await probe.MeasureAsync(null, cancellationToken).ConfigureAwait(false);
        if (_users++ == 0)
        {
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        lock (held)
        {
            held.Add(model.Key);
        }

        return new GpuLease(model, true, memory, lease => ReleaseAsync(lease, held));
    }

    private async ValueTask ReleaseAsync(GpuLease lease, List<string> held)
    {
        lock (held)
        {
            held.Remove(lease.Model.Key);
        }

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_users > 0 && --_users == 0)
            {
                _idle.TrySetResult();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Unloads <paramref name="model"/>; true when it is gone. A failure is logged and the model is kept to be asked again later; the card no
    /// longer counts it as the loaded model, and the next model measures what is really free. Called with the gate held.
    /// </summary>
    private async Task<bool> UnloadAsync(ILocalModel model, CancellationToken cancellationToken)
    {
        var gone = true;
        try
        {
            await model.UnloadAsync(cancellationToken).ConfigureAwait(false);
            _stuck.Remove(model);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            gone = false;
            if (!_stuck.Contains(model))
            {
                _stuck.Add(model);
            }

            logger.LogWarning("{Model} could not be unloaded and may still hold graphics memory ({Reason}); the next model runs with the memory that is free, "
                + "and the unload is asked again at the next change", model.Key, ex.Message);
        }

        if (ReferenceEquals(_loaded, model))
        {
            _loaded = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return gone;
    }

    /// <summary>
    /// Asks the models whose unload failed once more, except <paramref name="keep"/> (about to be used) and <paramref name="justAsked"/> (its
    /// unload was asked a moment ago: a server that waits out its unload time is not waited for twice). Called with the gate held.
    /// </summary>
    private async Task RetryStuckAsync(ILocalModel? keep, ILocalModel? justAsked, CancellationToken cancellationToken)
    {
        foreach (var stuck in _stuck.Where(m => (keep is null || m.Key != keep.Key) && !ReferenceEquals(m, justAsked)).ToList())
        {
            if (await UnloadAsync(stuck, cancellationToken).ConfigureAwait(false))
            {
                logger.LogInformation("{Model} was unloaded on a later try", stuck.Key);
            }
        }
    }

    /// <summary>The first card lease of the run: what local servers hold from before is freed (once; a failure is logged and the lease goes on).</summary>
    private async Task SweepOnceAsync(ILocalModel model, CancellationToken cancellationToken)
    {
        if (_swept)
        {
            return;
        }

        _swept = true;
        foreach (var sweep in _sweeps)
        {
            try
            {
                await sweep.SweepAsync(model, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                logger.LogWarning(ex, "What local servers held from before could not be freed");
            }
        }
    }

    /// <summary>
    /// Reads the card's memory until it reaches <paramref name="need"/>, stops rising (two readings one <see cref="GpuCoordinatorOptions.PollInterval"/>
    /// apart) or <see cref="GpuCoordinatorOptions.RecoveryTimeout"/> passes; an estimate (nothing measurable) is not waited for.
    /// </summary>
    private async Task<VramReading> WaitForMemoryAsync(ModelMemoryNeed? need, CancellationToken cancellationToken)
    {
        var target = need?.VideoMemoryBytes ?? 0;
        var deadline = clock.GetUtcNow() + options.RecoveryTimeout;
        long last = -1;
        while (true)
        {
            var reading = await probe.MeasureAsync(null, cancellationToken).ConfigureAwait(false);
            if (!reading.Measured || (target > 0 && reading.FreeBytes >= target) || (last >= 0 && reading.FreeBytes <= last) || clock.GetUtcNow() >= deadline)
            {
                return reading;
            }

            last = reading.FreeBytes;
            await Task.Delay(options.PollInterval, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
