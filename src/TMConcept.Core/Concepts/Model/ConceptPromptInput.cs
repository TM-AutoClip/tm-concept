using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// What a concept prompt is built from (<see cref="Memory"/>: the profile's past, see <see cref="PromptMemoryBuilder"/>; <see cref="Focus"/>:
/// the chosen focus drop-downs, <see cref="FocusText"/>; empty = none). <see cref="ExtraPrompt"/>, <see cref="Count"/> and <see cref="Topics"/>
/// come from the run's <see cref="ConceptBrief"/>: the topics of this batch, one concept each in order, or null for an idea run.
/// </summary>
public sealed record ConceptPromptInput(Profile Profile, IReadOnlyList<ProfileImage> Images, PromptMemory Memory, string ExtraPrompt, int Count,
    string Focus = "", IReadOnlyList<string>? Topics = null);
