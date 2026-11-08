using TMConcept.Core.Templates;

namespace TMConcept.Core.Markers;

/// <summary>
/// A placeholder the user can put into a text from a drop-down: its id, its kind, the token in each language (both spellings are understood
/// whatever the interface language is) and a short hint for the list; what the program does with it is one of <see cref="PauseMs"/>,
/// <see cref="Speed"/> and <see cref="Transition"/>.
/// </summary>
public sealed record Marker(string Id, MarkerKind Kind, LocalizedText Token, LocalizedText Hint)
{
    /// <summary>Silence after the text before it (narration; 0 = none).</summary>
    public int PauseMs { get; init; }

    /// <summary>Voice speed factor for the text after it until the next speed marker (narration; 0 = not a speed marker).</summary>
    public double Speed { get; init; }

    /// <summary>How the scene begins (<see cref="Scenes.SceneTransitions"/>; empty = not a transition marker).</summary>
    public string Transition { get; init; } = string.Empty;

    /// <summary>The token of a language ("tr" → Turkish, anything else → English).</summary>
    public string TokenOf(string language) => language == Profiles.ContentLanguages.Turkish ? Token.Tr : Token.En;

    /// <summary>The hint of a language ("tr" → Turkish, anything else → English).</summary>
    public string HintOf(string language) => language == Profiles.ContentLanguages.Turkish ? Hint.Tr : Hint.En;
}
