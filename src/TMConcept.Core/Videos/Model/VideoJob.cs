using TMConcept.Core.Concepts;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Videos;

/// <summary>
/// One video to make: the concept (with its text), the profile, the settings and the resolved code model; (F10, 0.1.49) the page that answers
/// a plan preview when the template asks for one (none = nothing waits, as before).
/// </summary>
public sealed record VideoJob(Concept Concept, Profile Profile, VideoOptions Options, ResolvedModel CodeModel)
{
    /// <summary>Who approves a previewed plan (the video page); null = the plan is never shown.</summary>
    public IPlanReview? Review { get; init; }
}
