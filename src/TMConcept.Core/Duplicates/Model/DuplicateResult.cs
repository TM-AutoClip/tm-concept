using System.Globalization;

namespace TMConcept.Core.Duplicates;

/// <summary>The heaviest match of a concept against the earlier ones.</summary>
public sealed record DuplicateResult(DuplicateKind Kind, double Similarity, Guid? OtherId, int OtherNumber, string OtherTitle)
{
    /// <summary>No match.</summary>
    public static DuplicateResult None { get; } = new(DuplicateKind.New, 0, null, 0, string.Empty);

    /// <summary>Whether the concept is kept.</summary>
    public bool Accepted => Kind != DuplicateKind.Duplicate;

    /// <summary>
    /// Language-neutral note stored with the concept (the interface turns it into text): <c>similar:212:0.71</c>,
    /// <c>continuation:7</c>, empty when new.
    /// </summary>
    public string Code => Kind switch
    {
        DuplicateKind.Similar => string.Create(CultureInfo.InvariantCulture, $"similar:{OtherNumber}:{Similarity:0.00}"),
        DuplicateKind.Continuation => string.Create(CultureInfo.InvariantCulture, $"continuation:{OtherNumber}"),
        DuplicateKind.Duplicate => string.Create(CultureInfo.InvariantCulture, $"duplicate:{OtherNumber}:{Similarity:0.00}"),
        _ => string.Empty,
    };
}
