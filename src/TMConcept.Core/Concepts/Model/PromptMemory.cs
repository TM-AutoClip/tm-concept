namespace TMConcept.Core.Concepts;

/// <summary>
/// What the prompts tell the model about a profile's past, already written as list lines ("\n- …", so a template line holding an empty one
/// is left out): <see cref="Covered"/> the concepts already made (newest one by one, older ones condensed, capped), the topic and telling
/// dislikes (trashed with a reason) and the user's AI-edit wishes. <c>null</c> = nothing of that kind.
/// </summary>
/// <param name="Covered">The covered block.</param>
/// <param name="CoveredCount">How many concepts the covered block stands for (all of them, also the condensed and the cut ones).</param>
/// <param name="DislikedTopics">Titles of concepts trashed for their topic (newest first).</param>
/// <param name="DislikedTelling">Title and short summary of concepts trashed for their telling (newest first).</param>
/// <param name="Wishes">The newest AI-edit instructions (newest first).</param>
public sealed record PromptMemory(string? Covered, int CoveredCount, string? DislikedTopics, string? DislikedTelling, string? Wishes)
{
    /// <summary>A profile without a past.</summary>
    public static PromptMemory Empty { get; } = new(null, 0, null, null, null);
}
