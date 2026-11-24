namespace TMConcept.Core.Videos;

/// <summary>
/// The user's side of the plan preview (F10, 0.1.49; the template's <see cref="Styles.VideoStyle.PlanReview"/>): the video waits here after
/// its scene plan, and again after its first scene when <see cref="Styles.VideoStyle.FirstSceneReview"/> is on. The video page answers; the
/// window's thread is never held by the wait (the queue waits on this video, the page stays free). <see cref="NightMode"/> is read before every
/// wait: in night mode nobody is at the computer, so nothing waits (KURALLAR 19).
/// </summary>
public interface IPlanReview
{
    /// <summary>Night mode is on now (a wait is skipped as if approved).</summary>
    bool NightMode { get; }

    /// <summary>Shows the plan's still frames and waits for approval, notes per scene, or a rejection.</summary>
    Task<PlanReviewAnswer> PlanAsync(PlanPreview preview, CancellationToken cancellationToken);

    /// <summary>Shows the first scene drawn with its image and waits: true = go on with the other images, false = stop the video.</summary>
    Task<bool> FirstSceneAsync(PlanPreview preview, CancellationToken cancellationToken);
}
