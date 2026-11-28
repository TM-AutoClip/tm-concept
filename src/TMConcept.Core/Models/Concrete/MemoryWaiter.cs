using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;

namespace TMConcept.Core.Models;

/// <summary>
/// <see cref="IMemoryWaiter"/> with a <see cref="TimeProvider"/> (tests move a fake clock). The first reading comes
/// <see cref="MemoryWaitOptions.Settle"/> after the last unload (at once when nothing was unloaded or the time has passed); a second one
/// <see cref="MemoryWaitOptions.Recheck"/> later; then the user is asked (<see cref="IMemoryShortfallHandler"/>), unless the flow runs
/// unattended (<see cref="MemoryShortfallScope"/>) or there is nobody to ask: then the use fails with <see cref="MemoryShortfallException"/>.
/// "Try anyway" (a model in a process of its own, <see cref="MemoryShortfall.CanTryAnyway"/>) ends the wait without enough memory.
/// Retry waits exactly <see cref="MemoryWaitOptions.Retry"/> and measures again; the user decides how often. The caller's token ends a wait
/// and closes the question. Every step is reported (<see cref="IMemoryNotices"/>) and logged: the first reading is reported only after an
/// unload (a run of images measures before every picture; a reading that finds enough at once says nothing).
/// </summary>
public sealed class MemoryWaiter(TimeProvider clock, MemoryWaitOptions options, ILogger<MemoryWaiter> logger, IMemoryShortfallHandler? handler = null,
    IMemoryNotices? notices = null) : IMemoryWaiter
{
    /// <summary>The readings before the user is asked.</summary>
    public const int Readings = 2;

    /// <inheritdoc />
    public async Task WaitAsync(IModel model, DateTimeOffset? unloadedAt, Func<CancellationToken, Task<MemoryShortfall?>> measure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(measure);
        if (unloadedAt is { } at)
        {
            var settle = options.Settle - (clock.GetUtcNow() - at);
            if (settle > TimeSpan.Zero)
            {
                Report(new MemoryNotice(MemoryNoticeKind.Settling, model.Name, model.Key) { Wait = settle });
                await Task.Delay(settle, clock, cancellationToken).ConfigureAwait(false);
            }

            Report(new MemoryNotice(MemoryNoticeKind.Measuring, model.Name, model.Key) { Step = 1, Of = Readings });
        }

        var shortfall = await measure(cancellationToken).ConfigureAwait(false);
        if (shortfall is null)
        {
            return;
        }

        logger.LogInformation("{Model}: not enough memory at the first reading ({Figures}); measuring again in {Seconds:0.0} s", model.Key,
            MemoryShortfallException.Describe(shortfall), options.Recheck.TotalSeconds);
        await Task.Delay(options.Recheck, clock, cancellationToken).ConfigureAwait(false);
        Report(new MemoryNotice(MemoryNoticeKind.Measuring, model.Name, model.Key) { Step = 2, Of = Readings });
        shortfall = await measure(cancellationToken).ConfigureAwait(false);
        while (shortfall is not null)
        {
            if (handler is null || MemoryShortfallScope.IsUnattended)
            {
                // Night mode: nobody is at the computer; the video fails and the queue goes on (or stops after three failures in a row).
                logger.LogError(LogEvents.OnScreen, "{Model}: not enough memory after {Readings} readings; nobody is asked (night mode): {Figures}", model.Key,
                    Readings, MemoryShortfallException.Describe(shortfall));
                Report(new MemoryNotice(MemoryNoticeKind.Failed, model.Name, model.Key) { Shortfall = shortfall });
                throw new MemoryShortfallException(shortfall);
            }

            logger.LogWarning(LogEvents.OnScreen, "{Model}: not enough memory after {Readings} readings; the user is asked: {Figures}", model.Key, Readings,
                MemoryShortfallException.Describe(shortfall));
            Report(new MemoryNotice(MemoryNoticeKind.Asking, model.Name, model.Key) { Shortfall = shortfall });
            var choice = await handler.AskAsync(shortfall, cancellationToken).ConfigureAwait(false);
            if (choice == MemoryShortfallChoice.TryAnyway && shortfall.CanTryAnyway)
            {
                // K24-a: the user knows it may spill into shared memory and be very slow; the coordinator reports the placement.
                logger.LogWarning(LogEvents.OnScreen, "{Model}: the user chose Try anyway with too little graphics memory ({Figures}); it may run very slowly",
                    model.Key, MemoryShortfallException.Describe(shortfall));
                return;
            }

            if (choice != MemoryShortfallChoice.Retry)
            {
                logger.LogWarning(LogEvents.OnScreen, "{Model}: the user canceled at the memory question; the video queue stops", model.Key);
                Report(new MemoryNotice(MemoryNoticeKind.Canceled, model.Name, model.Key) { Shortfall = shortfall });
                throw new MemoryShortfallCanceledException(shortfall);
            }

            logger.LogInformation("{Model}: the user chose Retry; measuring again in {Seconds:0.0} s", model.Key, options.Retry.TotalSeconds);
            Report(new MemoryNotice(MemoryNoticeKind.Retrying, model.Name, model.Key) { Wait = options.Retry });
            await Task.Delay(options.Retry, clock, cancellationToken).ConfigureAwait(false);
            shortfall = await measure(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("{Model}: enough memory now", model.Key);
        Report(new MemoryNotice(MemoryNoticeKind.Enough, model.Name, model.Key));
    }

    private void Report(MemoryNotice notice) => notices?.Report(notice);
}
