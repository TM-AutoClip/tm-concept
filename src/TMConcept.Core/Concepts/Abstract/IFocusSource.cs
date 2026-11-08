namespace TMConcept.Core.Concepts;

/// <summary>The optional fine-tuning drop-downs of the concept page (tone, audience, structure), defined as data.</summary>
public interface IFocusSource
{
    /// <summary>Every drop-down, in the order shown.</summary>
    IReadOnlyList<FocusDimension> Dimensions { get; }
}
