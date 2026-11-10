using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// What the "Suggest topics" prompt is built from: the channel, the interface language the topics are written in (they go into the extra
/// request the user writes), the chosen focus line, the extra request so far, the profile's past and the newest earlier suggestions.
/// </summary>
public sealed record TopicPromptInput(Profile Profile, string Language, string Focus, string ExtraPrompt, PromptMemory Memory,
    IReadOnlyList<string> EarlierSuggestions, int Count);
