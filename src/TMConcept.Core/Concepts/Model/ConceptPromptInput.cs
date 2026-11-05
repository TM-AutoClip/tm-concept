using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>What a concept prompt is built from.</summary>
public sealed record ConceptPromptInput(Profile Profile, IReadOnlyList<ProfileImage> Images, IReadOnlyList<string> HistoryTitles, string ExtraPrompt, int Count);
