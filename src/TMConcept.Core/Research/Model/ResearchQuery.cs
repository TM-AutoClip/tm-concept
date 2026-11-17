namespace TMConcept.Core.Research;

/// <summary>
/// One search of one backend: the subject's text, the language to search in ("tr", "en", …), how many hits at most and, for the addresses
/// backend, the user's addresses (no search).
/// </summary>
public sealed record ResearchQuery(string Text, string Language, int MaxHits)
{
    /// <summary>The addresses the user gave (only the addresses backend reads them).</summary>
    public IReadOnlyList<string> Urls { get; init; } = [];
}
