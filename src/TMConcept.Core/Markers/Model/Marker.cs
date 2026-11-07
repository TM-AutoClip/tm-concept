using TMConcept.Core.Templates;

namespace TMConcept.Core.Markers;

/// <summary>
/// A placeholder the user can put into a text from a drop-down: its id, where it is offered, the token in each language (both spellings are
/// understood whatever the interface language is), a short hint for the list, and what it means — for a prompt marker the sentence the
/// model is given (in the prompt's language), for a narration marker the silence it adds or the voice speed it sets.
/// </summary>
public sealed record Marker(string Id, MarkerKind Kind, LocalizedText Token, LocalizedText Hint, LocalizedText Meaning)
{
    /// <summary>Silence after the text before it (narration; 0 = none).</summary>
    public int PauseMs { get; init; }

    /// <summary>Voice speed factor for the text after it until the next speed marker (narration; 0 = not a speed marker).</summary>
    public double Speed { get; init; }

    /// <summary>The token of a language ("tr" → Turkish, anything else → English).</summary>
    public string TokenOf(string language) => language == Profiles.ContentLanguages.Turkish ? Token.Tr : Token.En;
}
