using TMConcept.Core.Documents;

namespace TMConcept.Core.Research;

/// <summary>
/// A place "Araştırmadan" (0.1.22) searches and fetches from: Wikipedia, Wikidata, the user's addresses, a keyed web search, or a tool of
/// the user's MCP server. The app calls it itself in a fixed plan (<see cref="ResearchRun"/>: search, show, fetch the ticked pages); no model
/// ever drives a backend, so a small local model needs no tool calling and the cost stays predictable. What it fetches is data: it is
/// cleaned (<see cref="ResearchText.Clean"/>), saved into the profile's documents and sent to the model only as passages inside the
/// documents prompt's data block.
/// </summary>
public interface IResearchBackend
{
    /// <summary>A stable id ("wikipedia", "wikidata", "url", "tavily", "mcp:&lt;id&gt;").</summary>
    string Id { get; }

    /// <summary>What it is.</summary>
    ResearchBackendKind Kind { get; }

    /// <summary>The name shown to the user.</summary>
    string Name { get; }

    /// <summary>Whether it needs the user's own key.</summary>
    bool NeedsKey { get; }

    /// <summary>Whether it is searched once per searched language (Wikipedia, Wikidata) rather than once per run.</summary>
    bool PerLanguage { get; }

    /// <summary>
    /// The hits for <paramref name="query"/>, the best first, at most <see cref="ResearchQuery.MaxHits"/>.
    /// <see cref="DocumentRejectedException"/> with a code when the backend cannot answer (network, key, rate limit).
    /// </summary>
    Task<IReadOnlyList<ResearchHit>> SearchAsync(ResearchQuery query, CancellationToken cancellationToken);

    /// <summary>The page or text of <paramref name="hit"/> as a document to save; <see cref="DocumentRejectedException"/> when it cannot be used.</summary>
    Task<FetchedDocument> FetchAsync(ResearchHit hit, CancellationToken cancellationToken);
}
