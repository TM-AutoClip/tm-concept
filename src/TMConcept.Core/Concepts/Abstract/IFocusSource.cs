namespace TMConcept.Core.Concepts;

/// <summary>The focus drop-downs of the concept page (category → period, region, tone…), defined as data.</summary>
public interface IFocusSource
{
    /// <summary>Every category, the general one first.</summary>
    IReadOnlyList<FocusCategory> Categories { get; }

    /// <summary>The category of a profile's template id ("history.byzantine" → history); null when there is none.</summary>
    FocusCategory? ForTemplate(string? templateId);
}
