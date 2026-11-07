using TMConcept.Core.Templates;

namespace TMConcept.Core.Concepts;

/// <summary>
/// A channel category with its focus drop-downs. Its id is the first part of a template id ("history" of "history.byzantine"), so a profile
/// made from a template starts with its category; <see cref="FocusCatalogFile.GeneralId"/> holds the drop-downs every category shows.
/// </summary>
public sealed class FocusCategory
{
    /// <summary>Id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Name in both languages.</summary>
    public LocalizedText Name { get; set; } = new();

    /// <summary>The drop-downs.</summary>
    public List<FocusDimension> Dimensions { get; set; } = [];
}
