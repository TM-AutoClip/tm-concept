namespace TMConcept.Core.Duplicates;

/// <summary>
/// Thresholds of the local duplicate check (not measured on real lists yet; tuned later). Text similarity = average of the word-set Jaccard
/// and the letter-trigram Dice of the titles, mixed with the summary words.
/// </summary>
public static class DuplicateThresholds
{
    /// <summary>At or above: the same concept (dropped).</summary>
    public const double Duplicate = 0.82;

    /// <summary>At or above: similar (badge).</summary>
    public const double Similar = 0.60;

    /// <summary>Two series names this close (trigram Dice) are the same series.</summary>
    public const double SameSeries = 0.80;

    /// <summary>Weight of the summary words when both summaries are long enough.</summary>
    public const double SummaryWeight = 0.3;

    /// <summary>Semantic check (embeddings): cosine at or above = duplicate.</summary>
    public const double SemanticDuplicate = 0.90;

    /// <summary>Semantic check (embeddings): cosine at or above = similar.</summary>
    public const double SemanticSimilar = 0.82;
}
