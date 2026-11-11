using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// What the "Suggest topics" prompt is built from: the channel, the interface language the topics are written in (the user reads and ticks
/// them), the chosen focus line, the profile's past, the newest earlier suggestions and how many to suggest. The extra request is not sent
/// (0.1.12): it used to pull topics outside the channel's scope.
/// </summary>
public sealed record TopicPromptInput(Profile Profile, string Language, string Focus, PromptMemory Memory, IReadOnlyList<string> EarlierSuggestions,
    int Count);
