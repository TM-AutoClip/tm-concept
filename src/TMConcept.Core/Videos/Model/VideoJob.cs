using TMConcept.Core.Concepts;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Videos;

/// <summary>One video to make: the concept (with its text), the profile, the settings and the resolved code model.</summary>
public sealed record VideoJob(Concept Concept, Profile Profile, VideoOptions Options, ResolvedModel CodeModel);
