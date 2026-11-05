namespace TMConcept.Core.Duplicates;

/// <summary>Comparison features of one concept, computed once (a large history is compared fast).</summary>
public sealed class DuplicateFeatures
{
    /// <summary>The concept.</summary>
    public required DuplicateSubject Subject { get; init; }

    /// <summary>Folded title without the part marker.</summary>
    public required string BaseTitle { get; init; }

    /// <summary>Title word stems.</summary>
    public required HashSet<string> Words { get; init; }

    /// <summary>Title letter trigrams.</summary>
    public required HashSet<string> Trigrams { get; init; }

    /// <summary>Summary word stems.</summary>
    public required HashSet<string> SummaryWords { get; init; }

    /// <summary>Part number (0 = none).</summary>
    public required int Part { get; init; }
}
