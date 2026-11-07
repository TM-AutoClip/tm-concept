using TMConcept.Core.Templates;

namespace TMConcept.Core.Concepts;

/// <summary>One choice of a focus drop-down ("Late Middle Ages (1300–1500)"); its name in the content language goes into the prompt.</summary>
public sealed class FocusOption
{
    /// <summary>Id inside its dimension.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Name in both languages.</summary>
    public LocalizedText Name { get; set; } = new();
}
