using TMConcept.Core.Templates;

namespace TMConcept.Core.Concepts;

/// <summary>One fine-tuning drop-down ("Tone", "Audience", "Structure") and its choices.</summary>
public sealed class FocusDimension
{
    /// <summary>Id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Name in both languages.</summary>
    public LocalizedText Name { get; set; } = new();

    /// <summary>The choices.</summary>
    public List<FocusOption> Options { get; set; } = [];
}
