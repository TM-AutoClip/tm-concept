using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// What a concept prompt is built from (<see cref="Memory"/>: the profile's past, see <see cref="PromptMemoryBuilder"/>; <see cref="Focus"/>:
/// the chosen focus drop-downs, <see cref="FocusText"/>; empty = none; <see cref="Topics"/>: the selected topics of this batch, one concept
/// each in order; null or empty = none, the count alone).
/// </summary>
public sealed record ConceptPromptInput(Profile Profile, IReadOnlyList<ProfileImage> Images, PromptMemory Memory, string ExtraPrompt, int Count,
    string Focus = "", IReadOnlyList<string>? Topics = null);
