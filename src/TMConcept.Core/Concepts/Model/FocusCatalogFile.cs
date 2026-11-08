namespace TMConcept.Core.Concepts;

/// <summary>
/// The optional fine-tuning drop-downs of the concept page as data (embedded <c>Concepts/Files/focus.json</c>): only general choices that
/// need no knowledge of the subject (tone, audience, structure). Period, region or field are not asked: the model takes them from the
/// channel concept and the extra request.
/// </summary>
public sealed class FocusCatalogFile
{
    /// <summary>The drop-downs.</summary>
    public List<FocusDimension> Dimensions { get; set; } = [];
}
