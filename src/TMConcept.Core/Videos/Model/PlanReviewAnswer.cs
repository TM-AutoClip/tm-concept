namespace TMConcept.Core.Videos;

/// <summary>The user's answer to a plan preview: approve, plan again with the notes (all of them in one call), or reject the video.</summary>
public sealed record PlanReviewAnswer(PlanReviewDecision Decision, IReadOnlyList<SceneNote> Notes)
{
    /// <summary>Approved as it is.</summary>
    public static PlanReviewAnswer Approve { get; } = new(PlanReviewDecision.Approve, []);

    /// <summary>Rejected: the video stops, the images are never paid for.</summary>
    public static PlanReviewAnswer Reject { get; } = new(PlanReviewDecision.Reject, []);
}
