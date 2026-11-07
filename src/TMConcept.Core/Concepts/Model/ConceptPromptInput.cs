using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>What a concept prompt is built from (<see cref="Focus"/>: the chosen focus drop-downs, <see cref="FocusText"/>; empty = none).</summary>
public sealed record ConceptPromptInput(Profile Profile, IReadOnlyList<ProfileImage> Images, IReadOnlyList<string> HistoryTitles, string ExtraPrompt, int Count,
    string Focus = "");
