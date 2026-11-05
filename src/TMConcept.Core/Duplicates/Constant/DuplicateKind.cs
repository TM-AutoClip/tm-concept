namespace TMConcept.Core.Duplicates;

/// <summary>Result of comparing a new concept with earlier ones (ordered by weight: Duplicate is the heaviest).</summary>
public enum DuplicateKind
{
    /// <summary>Nothing similar.</summary>
    New = 0,

    /// <summary>Same subject, a different part of a series ("… — Part 3"): accepted.</summary>
    Continuation = 1,

    /// <summary>Similar but not the same: accepted with a badge; the user decides.</summary>
    Similar = 2,

    /// <summary>The same concept: dropped.</summary>
    Duplicate = 3,
}
