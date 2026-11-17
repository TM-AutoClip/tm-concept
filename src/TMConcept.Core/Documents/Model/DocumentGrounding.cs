namespace TMConcept.Core.Documents;

/// <summary>
/// The document passages a concept call is grounded in (picked for the call within the model's context, see <see cref="PassageRetriever"/>):
/// the documents they come from, numbered, and the passages in the order they are sent.
/// </summary>
public sealed record DocumentGrounding(IReadOnlyList<GroundingDocument> Documents, IReadOnlyList<GroundingPassage> Passages)
{
    /// <summary>Whether a search found the passages (false: no query, or nothing matched it, so they were spread over the documents).</summary>
    public bool Matched { get; init; }

    /// <summary>No passages yet (a documents prompt measured before its passages are picked).</summary>
    public static DocumentGrounding Empty { get; } = new([], []);

    /// <summary>The titles of <paramref name="numbers"/> (unknown numbers are left out), each once, in the order given.</summary>
    public IReadOnlyList<string> Titles(IEnumerable<int> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        return numbers.Distinct().Select(n => Documents.FirstOrDefault(d => d.Number == n)?.Title).OfType<string>().ToList();
    }
}
