namespace TMConcept.Core.Videos;

/// <summary>The end of a run of the video queue.</summary>
/// <param name="End">Why it ended.</param>
/// <param name="Made">Videos made.</param>
/// <param name="Failed">Videos that failed.</param>
public sealed record QueueRunResult(QueueRunEnd End, int Made, int Failed);
