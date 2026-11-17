using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;

namespace TMConcept.Core.Models;

/// <summary>
/// The app's one graphics card queue (see <see cref="IGpuCoordinator"/>). The model on the card stays after its lease ends (the next batch of
/// the same model needs no reload); it is unloaded only when another local model asks for the card, or by <see cref="FreeAsync"/>. Before the
/// first card lease of a run, every <see cref="IResidentModelSweep"/> frees what local servers still hold from before. A model whose unload
/// could not be confirmed (<see cref="ModelUnloadException"/>, or any other failure) is not forgotten: it is asked again at every later change
/// of model and by <see cref="FreeAsync"/>; meanwhile the next model gets the card with the memory measured after the failed unload.
/// <para>Every handover (a model that is not on the card yet) runs the memory check of <see cref="IMemoryWaiter"/> with the model's own plan
/// (<see cref="ILocalModel.PlanMemory"/>): 3 s after the last unload (a swap here, or <see cref="FreeAsync"/> at the end of a video: 0.1.17
/// skipped the wait there), a second reading 2.5 s later, then the user's question. The check runs OUTSIDE the coordinator's lock with the
/// lease already counted, so an open question keeps other models waiting for the card (as any lease does) but never blocks a release or
/// <see cref="FreeAsync"/>; a second lease of the same model waits for the running check. A placement off the card (main memory, disk) is
/// reported (<see cref="IMemoryNotices"/>) and logged as a warning. Every reading of a check after its first asks the stuck models again
/// (<see cref="RetryStuckAsync(IModel, CancellationToken)"/>): while the check holds the card nothing else would, so the user's Retry would
/// only measure our own server's memory again; the question names the models still stuck.</para>
/// Waiting leases are not ordered. Each change is logged (model codes and memory figures only).
/// </summary>
public sealed class GpuCoordinator(IModelDirectory directory, IVramProbe probe, IMemoryWaiter waiter, TimeProvider clock, ILogger<GpuCoordinator> logger,
    IEnumerable<IResidentModelSweep>? sweeps = null, ISystemMemory? memory = null, IMemoryNotices? notices = null)
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

    // Whether the model on the card passed its memory check; the check of it that is running now (a second lease of it waits for it).
    private bool _verified;
    private TaskCompletionSource? _checking;
    private DateTimeOffset? _unloadedAt;
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> RetryStuckAsync(IModel keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keep);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RetryStuckAsync(keep, null, cancellationToken).ConfigureAwait(false);
            return _stuck.Where(m => m.Key != keep.Key).Select(m => m.Name).ToList();
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
            TaskCompletionSource? check = null;
            DateTimeOffset? unloadedAt = null;
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
                        logger.LogInformation("Graphics card: {Previous} unloaded for {Model}", previous.Key, model.Key);
                    }
                }
                else if (_loaded is null)
                {
                    await SweepOnceAsync(model, cancellationToken).ConfigureAwait(false);
                    await RetryStuckAsync(model, null, cancellationToken).ConfigureAwait(false);
                }

                if (busy is null)
                {
                    if (_loaded is not null && _verified)
                    {
                        return await GrantNowAsync(model, held, cancellationToken).ConfigureAwait(false);
                    }

                    if (_checking is { } running)
                    {
                        // Another flow is checking the memory for this same model: wait for it, then look again.
                        busy = running.Task;
                    }
                    else
                    {
                        // This flow checks: the model takes the card now (counted as a user, so no other model unloads it meanwhile), the
                        // check runs after the lock is released.
                        check = _checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        unloadedAt = _unloadedAt;
                        Reserve(model, held);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            if (check is not null)
            {
                return await CheckAndGrantAsync(model, need, held, unloadedAt, check, cancellationToken).ConfigureAwait(false);
            }

            await busy!.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The model takes the card before its check (called with the gate held): a user is counted, the flow holds its key.</summary>
    private void Reserve(ILocalModel model, List<string> held)
    {
        var changed = !ReferenceEquals(_loaded, model);
        _loaded = model;
        _verified = false;
        _stuck.Remove(model);
        if (_users++ == 0)
        {
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        lock (held)
        {
            held.Add(model.Key);
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The memory check of a handover, outside the gate: wait and measure (<see cref="IMemoryWaiter"/>), then load and hand out the lease. A
    /// failure, a cancellation or the user's Cancel gives the reserved lease back (no user count is left behind) and goes to the caller.
    /// </summary>
    private async Task<IGpuLease> CheckAndGrantAsync(ILocalModel model, ModelMemoryNeed? need, List<string> held, DateTimeOffset? unloadedAt,
        TaskCompletionSource check, CancellationToken cancellationToken)
    {
        var lease = new GpuLease(model, true, VramReading.None, l => ReleaseAsync(l, held));
        try
        {
            MemoryPlan? plan = null;
            var readings = 0;
            await waiter.WaitAsync(model, unloadedAt, async token =>
            {
                // The first reading follows the handover's own unload; every later one (the second reading, each Retry) asks our stuck
                // models again first: they may be what holds the card.
                IReadOnlyList<string> stuck = readings++ == 0 ? [] : await RetryStuckAsync(model, token).ConfigureAwait(false);
                var reading = await probe.MeasureAsync(null, token).ConfigureAwait(false);
                var free = memory?.AvailableBytes();
                plan = model.PlanMemory(new MemorySnapshot(reading, free), need);
                logger.LogInformation("{Model}: memory plan {Placement} ({Free:0.0} of {Total:0.0} GiB graphics memory free, {Source})", model.Key, plan.Placement,
                    reading.FreeBytes / Gibibyte, reading.TotalBytes / Gibibyte, reading.Source);
                return plan.Runs || !reading.Measured
                    ? null
                    : new MemoryShortfall(model.Name, reading.TotalBytes, Math.Max(reading.FreeBytes, reading.SystemFreeBytes ?? 0), reading.Source,
                        plan.NeededVideoBytes, free, plan.NeededMainBytes) { HeldBy = stuck };
            }, cancellationToken).ConfigureAwait(false);

            if (plan is { Slower: true })
            {
                logger.LogWarning(Activity.LogEvents.OnScreen, "{Model} does not fit on the graphics card; it uses {Placement} and runs slower", model.Key,
                    plan.Placement);
                notices?.Report(new MemoryNotice(MemoryNoticeKind.Placed, model.Name, model.Key) { Placement = plan.Placement });
            }

            if (!model.IsLoaded)
            {
                await model.LoadAsync(cancellationToken).ConfigureAwait(false);
            }

            var reading = await probe.MeasureAsync(null, cancellationToken).ConfigureAwait(false);
            await SettleCheckAsync(check, true).ConfigureAwait(false);
            return new GpuLease(model, true, reading, l => ReleaseAsync(l, held));
        }
        catch
        {
            await SettleCheckAsync(check, false).ConfigureAwait(false);
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Ends a check: the model counts as verified when it passed (and is still the one on the card); flows waiting for it look again.</summary>
    private async Task SettleCheckAsync(TaskCompletionSource check, bool passed)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_checking, check))
            {
                _checking = null;
                _verified = passed;
            }
        }
        finally
        {
            _gate.Release();
        }

        check.TrySetResult();
    }

    /// <summary>
    /// A lease of the model already on the card and checked (called with the gate held). The user count goes up only after the last step that
    /// can fail or be cancelled: a Stop during the load or the reading leaves no count behind (a count without a lease would keep every other
    /// local model waiting until the app is restarted).
    /// </summary>
    private async Task<IGpuLease> GrantNowAsync(ILocalModel model, List<string> held, CancellationToken cancellationToken)
    {
        if (!model.IsLoaded)
        {
            await model.LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        var reading = await probe.MeasureAsync(null, cancellationToken).ConfigureAwait(false);
        if (_users++ == 0)
        {
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        lock (held)
        {
            held.Add(model.Key);
        }

        return new GpuLease(model, true, reading, lease => ReleaseAsync(lease, held));
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
    /// longer counts it as the loaded model, and the next model measures what is really free. The time is kept: the next model's memory check
    /// waits for the driver from then on. Called with the gate held.
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

        _unloadedAt = clock.GetUtcNow();
        if (ReferenceEquals(_loaded, model))
        {
            _loaded = null;
            _verified = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return gone;
    }

    /// <summary>
    /// Asks the models whose unload failed once more, except <paramref name="keep"/> (about to be used) and <paramref name="justAsked"/> (its
    /// unload was asked a moment ago: a server that waits out its unload time is not waited for twice). Called with the gate held.
    /// </summary>
    private async Task RetryStuckAsync(IModel? keep, ILocalModel? justAsked, CancellationToken cancellationToken)
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

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
