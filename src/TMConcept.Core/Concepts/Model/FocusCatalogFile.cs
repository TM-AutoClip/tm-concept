namespace TMConcept.Core.Concepts;

/// <summary>The focus drop-downs of the concept page as data (embedded <c>Concepts/Files/focus.json</c>).</summary>
public sealed class FocusCatalogFile
{
    /// <summary>The category whose drop-downs are shown for every channel (tone, audience, structure).</summary>
    public const string GeneralId = "general";

    /// <summary>The categories, the general one first.</summary>
    public List<FocusCategory> Categories { get; set; } = [];
}
