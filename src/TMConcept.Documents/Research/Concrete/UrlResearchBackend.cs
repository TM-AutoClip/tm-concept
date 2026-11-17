using TMConcept.Core.Documents;
using TMConcept.Core.Research;
using TMConcept.Documents.Web;

namespace TMConcept.Documents.Research;

/// <summary>
/// The addresses the user gave (no search, no network until fetched): each one is a candidate, always picked first. A Wikipedia article's
/// address is fetched as an article (plain text with its headings), any other address as a web page's readable text or a PDF, through the
/// same guarded fetchers as "Belgelerim" (public addresses only, size and time limits, no script ever runs).
/// </summary>
public sealed class UrlResearchBackend(WebPageFetcher web, WikipediaFetcher wikipedia) : ResearchBackendBase
{
    /// <inheritdoc />
    public override string Id => ResearchRun.UrlBackendId;

    /// <inheritdoc />
    public override ResearchBackendKind Kind => ResearchBackendKind.Url;

    /// <inheritdoc />
    public override string Name => "URL";

    /// <inheritdoc />
    protected override bool NeedsText => false;

    /// <inheritdoc />
    protected override Task<IReadOnlyList<ResearchHit>> SearchCoreAsync(ResearchQuery query, CancellationToken cancellationToken)
    {
        var hits = new List<ResearchHit>();
        foreach (var text in query.Urls.Take(ResearchLimits.UrlsMax))
        {
            if (DocumentFetcherBase.WebAddress(text) is not { } uri)
            {
                continue;
            }

            var article = uri.Host.EndsWith("wikipedia.org", StringComparison.OrdinalIgnoreCase) ? WikipediaFetcher.Article(uri.ToString(), string.Empty) : null;
            hits.Add(article is { } found
                ? new ResearchHit(string.Empty, found.Title, uri.ToString(), found.Language)
                : new ResearchHit(string.Empty, uri.Host + uri.AbsolutePath.TrimEnd('/'), uri.ToString(), string.Empty));
        }

        return Task.FromResult<IReadOnlyList<ResearchHit>>(hits.DistinctBy(h => ResearchText.UrlKey(h.Url)).ToList());
    }

    /// <inheritdoc />
    protected override Task<FetchedDocument> FetchCoreAsync(ResearchHit hit, CancellationToken cancellationToken) =>
        hit.Language.Length > 0 && WikipediaFetcher.Article(hit.Url, hit.Language) is not null
            ? wikipedia.FetchAsync(hit.Url, hit.Language, cancellationToken)
            : web.FetchAsync(hit.Url, string.Empty, cancellationToken);
}
