using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;
using TMConcept.Core.Research;
using TMConcept.Documents.Web;

namespace TMConcept.Documents.Research;

/// <summary>
/// Wikipedia in any language (on by default, no key). ONE request of the Action API searches and, for every found article, gives its
/// address, short description, Wikidata item (the key that makes the same entity in many languages one source group) and its language links
/// (the same article in the other read languages, fetched by its own title). The article itself is fetched like "Belgelerim" fetches it
/// (<see cref="WikipediaFetcher"/>: plain text with its headings).
/// </summary>
public sealed class WikipediaResearchBackend(DocumentTransport transport, WikipediaFetcher fetcher, ILogger<WikipediaResearchBackend> logger)
    : HttpResearchBackendBase(transport, logger)
{
    /// <inheritdoc />
    public override string Id => "wikipedia";

    /// <inheritdoc />
    public override ResearchBackendKind Kind => ResearchBackendKind.Wikipedia;

    /// <inheritdoc />
    public override string Name => "Wikipedia";

    /// <inheritdoc />
    public override bool PerLanguage => true;

    /// <summary>The search address of <paramref name="text"/> on the Wikipedia of <paramref name="language"/>.</summary>
    public static Uri SearchAddress(string language, string text, int count) => new(string.Create(CultureInfo.InvariantCulture,
        $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&generator=search&gsrnamespace=0&gsrlimit={count}&gsrsearch={Uri.EscapeDataString(text)}&prop=pageprops%7Cinfo%7Cdescription%7Clanglinks&ppprop=wikibase_item&inprop=url&lllimit=max"));

    /// <summary>The address of an article of <paramref name="language"/>'s Wikipedia.</summary>
    public static string ArticleAddress(string language, string title) =>
        $"https://{language}.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";

    /// <summary>The hits of a search answer, in Wikipedia's order (the backend's id is filled in later).</summary>
    public static IReadOnlyList<ResearchHit> ParseSearch(JsonElement root, string language)
    {
        if (!root.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var hits = new List<(int Index, ResearchHit Hit)>();
        foreach (var page in pages.EnumerateArray())
        {
            var title = Text(page, "title");
            if (title.Length == 0 || page.TryGetProperty("missing", out _))
            {
                continue;
            }

            var index = page.TryGetProperty("index", out var at) && at.TryGetInt32(out var i) ? i : int.MaxValue;
            var item = page.TryGetProperty("pageprops", out var props) ? Text(props, "wikibase_item") : string.Empty;
            var links = new List<ResearchAlternate>();
            if (page.TryGetProperty("langlinks", out var langlinks) && langlinks.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in langlinks.EnumerateArray())
                {
                    if (ResearchText.Language(Text(link, "lang")) is { } code && Text(link, "title") is { Length: > 0 } other)
                    {
                        links.Add(new ResearchAlternate(code, other, ArticleAddress(code, other)));
                    }
                }
            }

            var url = Text(page, "fullurl");
            hits.Add((index, new ResearchHit(string.Empty, title, url.Length > 0 ? url : ArticleAddress(language, title), language)
            {
                Snippet = Text(page, "description"),
                EntityKey = item.Length > 0 ? item : string.Empty,
                Alternates = links,
            }));
        }

        return hits.OrderBy(h => h.Index).Select(h => h.Hit).ToList();
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<ResearchHit>> SearchCoreAsync(ResearchQuery query, CancellationToken cancellationToken)
    {
        var language = ResearchText.Language(query.Language) ?? "en";
        var response = await Http.GetAsync(SearchAddress(language, query.Text, query.MaxHits), cancellationToken).ConfigureAwait(false);
        using var json = Json(response.Body);
        return ParseSearch(json.RootElement, language);
    }

    /// <inheritdoc />
    protected override Task<FetchedDocument> FetchCoreAsync(ResearchHit hit, CancellationToken cancellationToken) =>
        fetcher.FetchAsync(hit.Url, hit.Language, cancellationToken);
}
