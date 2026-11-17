namespace TMConcept.Core.Videos;

/// <summary>
/// The stop rules of the video queue (0.1.18, the developer). Night mode OFF: any failed video stops the whole queue (the error is on screen,
/// in a notification and in red in the log; the user starts the queue again). Night mode ON (for when nobody is at the computer): a failed
/// video does not stop the queue, the next one is made; 10 s pass between videos; <see cref="VideoQueueRunner.FailuresInRow"/> failures in a
/// row stop it for good (no money is wasted), a made video starts the count again. The night mode is read at every decision, so ticking it
/// at video 50 counts from there. The memory question's Cancel stops the queue in both modes.
/// </summary>
public interface IVideoQueueRunner
{
    /// <summary>Runs the queue until it is empty or a rule stops it.</summary>
    /// <typeparam name="T">A queue item.</typeparam>
    /// <param name="next">The next item to make (the queue is read again every time; null = none left).</param>
    /// <param name="produce">Makes one item and says how it ended (it reports its own success or failure to the user).</param>
    /// <param name="nightMode">Reads the night mode checkbox now.</param>
    /// <param name="cancellationToken">The queue's Cancel.</param>
    Task<QueueRunResult> RunAsync<T>(Func<CancellationToken, Task<T?>> next, Func<T, CancellationToken, Task<QueueItemOutcome>> produce, Func<bool> nightMode,
        CancellationToken cancellationToken)
        where T : class;
}
