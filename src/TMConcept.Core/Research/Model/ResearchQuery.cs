namespace TMConcept.Core.Research;

/// <summary>
/// One search of one backend: the subject's text, the language to search in ("tr", "en", …), how many hits at most and, for the addresses
/// backend, the user's addresses (no search). <see cref="LinkLanguages"/>: the other read languages whose versions of the found articles
/// the run can use (a backend with language links asks only for these).
/// </summary>
public sealed record ResearchQuery(string Text, string Language, int MaxHits)
{
    /// <summary>The addresses the user gave (only the addresses backend reads them).</summary>
    public IReadOnlyList<string> Urls { get; init; } = [];

    /// <summary>The read languages a found article's other versions are wanted in (more preferred ones and the ones not searched directly).</summary>
    public IReadOnlyList<string> LinkLanguages { get; init; } = [];
}
