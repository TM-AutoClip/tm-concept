using TMConcept.Core.Templates;

namespace TMConcept.Core.Concepts;

/// <summary>One choice of a fine-tuning drop-down ("mysterious"); its name in the prompt's language goes into the prompt.</summary>
public sealed class FocusOption
{
    /// <summary>Id inside its dimension.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Name in both languages.</summary>
    public LocalizedText Name { get; set; } = new();
}
