using TMConcept.Core.Templates;

namespace TMConcept.Core.Concepts;

/// <summary>One focus drop-down of a category ("Period", "Region", "Tone") and its choices.</summary>
public sealed class FocusDimension
{
    /// <summary>Id inside its category.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Name in both languages.</summary>
    public LocalizedText Name { get; set; } = new();

    /// <summary>The choices.</summary>
    public List<FocusOption> Options { get; set; } = [];
}
