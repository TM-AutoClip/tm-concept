namespace TMConcept.Core.Videos;

/// <summary>Why a run of the video queue ended (<see cref="IVideoQueueRunner"/>).</summary>
public enum QueueRunEnd
{
    /// <summary>Every item was tried.</summary>
    Done = 1,

    /// <summary>Night mode off: a video failed and the whole queue stopped (the user starts it again).</summary>
    Failed = 2,

    /// <summary>Night mode on: <see cref="VideoQueueRunner.FailuresInRow"/> videos in a row failed and the queue stopped (no money is wasted).</summary>
    FailuresInRow = 3,

    /// <summary>The user stopped it from inside a video (Cancel in the memory question).</summary>
    Stopped = 4,

    /// <summary>The queue's Cancel was pressed.</summary>
    Canceled = 5,
}
