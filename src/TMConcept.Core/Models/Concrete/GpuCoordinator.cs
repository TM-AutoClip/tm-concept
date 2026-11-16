using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;

namespace TMConcept.Core.Models;

/// <summary>
/// The app's one graphics card queue (see <see cref="IGpuCoordinator"/>). The model on the card stays after its lease ends (the next batch of
/// the same model needs no reload); it is unloaded only when another local model asks for the card, or by <see cref="FreeAsync"/>. Waiting
/// leases are not ordered. Each change is logged (model codes and memory figures only).
/// </summary>
public sealed class GpuCoordinator(IModelDirectory directory, IVramProbe probe, TimeProvider clock, GpuCoordinatorOptions options, ILogger<GpuCoordinator> logger)
    : IGpuCoordinator
{
    private const double Gibibyte = 1024.0 * 1024 * 1024;

    // The keys of the card leases the current flow holds (a flow that asks for a second model while it holds one would wait for itself).
    private static readonly AsyncLocal<List<string>?> Held = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ILocalModel? _loaded;
    private int _users;
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
            if (_loaded is { } loaded && _users == 0)
            {
                await UnloadAsync(loaded, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Graphics card freed: {Model} unloaded", loaded.Key);
            }
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
                        var freed = await WaitForMemoryAsync(need, cancellationToken).ConfigureAwait(false);
                        logger.LogInformation("Graphics card: {Previous} unloaded for {Model}; {Free:0.0} of {Total:0.0} GiB free ({Source})", previous.Key, model.Key,
                            freed.FreeBytes / Gibibyte, freed.TotalBytes / Gibibyte, freed.Source);
                    }
                }

                if (busy is null)
                {
                    var changed = _loaded is null;
                    if (!model.IsLoaded)
                    {
                        await model.LoadAsync(cancellationToken).ConfigureAwait(false);
                    }

                    _loaded = model;
                    if (_users++ == 0)
                    {
                        _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    }

                    var memory = await probe.MeasureAsync(null, cancellationToken).ConfigureAwait(false);
                    lock (held)
                    {
                        held.Add(model.Key);
                    }

                    if (changed)
                    {
                        Changed?.Invoke(this, EventArgs.Empty);
                    }

                    return new GpuLease(model, true, memory, lease => ReleaseAsync(lease, held));
                }
            }
            finally
            {
                _gate.Release();
            }

            await busy.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
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

    /// <summary>Unloads <paramref name="model"/> (a failure is logged: the card is taken as free anyway); called with the gate held.</summary>
    private async Task UnloadAsync(ILocalModel model, CancellationToken cancellationToken)
    {
        try
        {
            await model.UnloadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            logger.LogWarning(ex, "{Model} could not be unloaded from the graphics card", model.Key);
        }

        _loaded = null;
        Changed?.Invoke(this, EventArgs.Empty);
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