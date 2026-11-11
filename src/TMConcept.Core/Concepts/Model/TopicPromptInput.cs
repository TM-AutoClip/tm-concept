using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// What the "Suggest topics" prompt is built from: the channel, the interface language the topics are written in (the user reads and picks
/// them), the chosen focus line, the profile's past, the newest earlier suggestions, the topics already in the selected list and how many to
/// suggest. The extra request is not sent (0.1.12): it used to pull topics outside the channel's scope.
/// </summary>
public sealed record TopicPromptInput(Profile Profile, string Language, string Focus, PromptMemory Memory, IReadOnlyList<string> EarlierSuggestions,
    IReadOnlyList<string> Selected, int Count);
