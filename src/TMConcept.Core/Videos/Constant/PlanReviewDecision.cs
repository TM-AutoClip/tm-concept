namespace TMConcept.Core.Videos;

/// <summary>What the user decided about a plan preview (F10, 0.1.49).</summary>
public enum PlanReviewDecision
{
    /// <summary>Go on: the images are made and the video rendered.</summary>
    Approve = 0,

    /// <summary>Plan again with the notes (one call for all of them), then show the new plan.</summary>
    Revise = 1,

    /// <summary>Stop the video; nothing more is paid for.</summary>
    Reject = 2,
}
