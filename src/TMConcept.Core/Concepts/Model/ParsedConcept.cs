namespace TMConcept.Core.Concepts;

/// <summary>
/// A concept read from a model answer (cleaned, not saved yet). <see cref="Sources"/>: the numbers of the documents a documents run's concept
/// names (empty otherwise; numbers that were not sent are dropped later).
/// </summary>
public sealed record ParsedConcept(string Title, string Summary, string Content)
{
    /// <summary>The document numbers the concept names.</summary>
    public IReadOnlyList<int> Sources { get; init; } = [];

    /// <summary>Equal when the texts and the document numbers (in order) are equal.</summary>
    public bool Equals(ParsedConcept? other) =>
        other is not null && Title == other.Title && Summary == other.Summary && Content == other.Content && Sources.SequenceEqual(other.Sources);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Title, Summary, Content, Sources.Count);
}
