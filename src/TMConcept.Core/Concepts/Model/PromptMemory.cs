namespace TMConcept.Core.Concepts;

/// <summary>
/// What the prompts tell the model about a profile's past, already written as list lines ("\n- …", so a template line holding an empty one
/// is left out): <see cref="Covered"/> the concepts already made (newest one by one, older ones condensed, capped), the topic and telling
/// dislikes (trashed with a reason), the user's AI-edit wishes and the rated videos (a disliked video is the strongest "stay away", a liked
/// one a positive example; a video without a rating is only covered). <c>null</c> = nothing of that kind.
/// </summary>
/// <param name="Covered">The covered block.</param>
/// <param name="CoveredCount">How many concepts the covered block stands for (all of them, also the condensed and the cut ones).</param>
/// <param name="DislikedTopics">Titles of concepts trashed for their topic (newest first).</param>
/// <param name="DislikedTelling">Title and short summary of concepts trashed for their telling (newest first).</param>
/// <param name="Wishes">The newest AI-edit instructions (newest first).</param>
/// <param name="DislikedVideos">Title and short summary of the videos the user did not like (newest first).</param>
/// <param name="LikedVideos">Title and short summary of the videos the user liked (newest first).</param>
public sealed record PromptMemory(string? Covered, int CoveredCount, string? DislikedTopics, string? DislikedTelling, string? Wishes,
    string? DislikedVideos = null, string? LikedVideos = null)
{
    /// <summary>A profile without a past.</summary>
    public static PromptMemory Empty { get; } = new(null, 0, null, null, null);
}
