namespace TMConcept.Core.Concepts;

/// <summary>
/// The three sources of the concept page (0.1.21). Each one adds to the same concept list, in whatever order the user uses them; none
/// depends on another.
/// </summary>
public enum ConceptSourceKind
{
    /// <summary>"Konu öner": the ticked suggested topics, one concept each.</summary>
    Topics = 0,

    /// <summary>"Fikirden": a free idea with a count.</summary>
    Idea = 1,

    /// <summary>"Belgelerden": the profile's documents, with an optional subject; N concepts, or one per ticked topic found in them.</summary>
    Documents = 2,
}
