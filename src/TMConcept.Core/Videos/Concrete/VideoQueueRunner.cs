using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;

namespace TMConcept.Core.Videos;

/// <summary>
/// <see cref="IVideoQueueRunner"/> with a <see cref="TimeProvider"/> for the night mode's gap (tests move a fake clock). Its stop and failure
/// lines are only for the log (<see cref="LogEvents.OnScreen"/>): the page already shows each failed video and the stop in the user's language.
/// </summary>
public sealed class VideoQueueRunner(TimeProvider clock, ILogger<VideoQueueRunner> logger) : IVideoQueueRunner
{
    /// <summary>Failed videos in a row that stop the queue in night mode.</summary>
    public const int FailuresInRow = 3;

    /// <summary>The wait between two videos in night mode (fixed, not a setting).</summary>
    public static readonly TimeSpan NightGap = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public async Task<QueueRunResult> RunAsync<T>(Func<CancellationToken, Task<T?>> next, Func<T, CancellationToken, Task<QueueItemOutcome>> produce,
        Func<bool> nightMode, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(produce);
        ArgumentNullException.ThrowIfNull(nightMode);
        int made = 0, failed = 0, inRow = 0;
        var started = false;
        QueueRunResult End(QueueRunEnd end) => new(end, made, failed);
        while (!cancellationToken.IsCancellationRequested)
        {
            if (started && nightMode())
            {
                logger.LogInformation("Night mode: {Seconds:0} s before the next video", NightGap.TotalSeconds);
                try
                {
                    await Task.Delay(NightGap, clock, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return End(QueueRunEnd.Canceled);
                }
            }

            var item = await next(cancellationToken).ConfigureAwait(false);
            if (item is null)
            {
                return End(QueueRunEnd.Done);
            }

            var outcome = await produce(item, cancellationToken).ConfigureAwait(false);
            started |= outcome is QueueItemOutcome.Made or QueueItemOutcome.Failed;
            switch (outcome)
            {
                case QueueItemOutcome.Made:
                    made++;
                    inRow = 0;
                    break;
                case QueueItemOutcome.Failed:
                    failed++;
                    inRow++;
                    if (!nightMode())
                    {
                        logger.LogWarning(LogEvents.OnScreen, "A video failed; the queue stops (night mode is off)");
                        return End(QueueRunEnd.Failed);
                    }

                    if (inRow >= FailuresInRow)
                    {
                        logger.LogError(LogEvents.OnScreen, "Night mode: {Count} videos in a row failed; the queue stops", inRow);
                        return End(QueueRunEnd.FailuresInRow);
                    }

                    logger.LogWarning(LogEvents.OnScreen, "Night mode: a video failed ({Count} in a row); the queue goes on with the next one", inRow);
                    break;
                case QueueItemOutcome.Stopped:
                    return End(QueueRunEnd.Stopped);
                case QueueItemOutcome.Canceled:
                    return End(QueueRunEnd.Canceled);
            }
        }

        return End(QueueRunEnd.Canceled);
    }
}
