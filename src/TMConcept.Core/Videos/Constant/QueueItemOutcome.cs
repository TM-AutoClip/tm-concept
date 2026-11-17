namespace TMConcept.Core.Videos;

/// <summary>How one queue item ended (<see cref="IVideoQueueRunner"/>).</summary>
public enum QueueItemOutcome
{
    /// <summary>The video was made.</summary>
    Made = 1,

    /// <summary>Not made and not a failure: removed from the queue meanwhile, or its concept is no longer queued.</summary>
    Skipped = 2,

    /// <summary>It failed (any error, a memory shortfall in night mode too).</summary>
    Failed = 3,

    /// <summary>The user stopped the queue from inside the item (Cancel in the memory question).</summary>
    Stopped = 4,

    /// <summary>The queue's Cancel was pressed.</summary>
    Canceled = 5,
}
