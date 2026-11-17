using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;
using TMConcept.Core.Research;
using TMConcept.Documents.Web;

namespace TMConcept.Documents.Research;

/// <summary>
/// Wikipedia in any language (on by default, no key). One request of the Action API searches and, for every found article, gives its
/// address, short description and Wikidata item (the key that makes the same entity in many languages one source group). Then one small
/// request per wanted language (<see cref="ResearchQuery.LinkLanguages"/>) gives the found articles' language link in that language only
/// (the same article in the other read languages, fetched by its own title): asking for every language link at once is cut at 500 links
/// for the whole answer, and a popular article alone has hundreds. A language whose links could not be read is logged and left out (the
/// hits stay). The article itself is fetched like "Belgelerim" fetches it (<see cref="WikipediaFetcher"/>: plain text with its headings).
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
        $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&maxlag=5&generator=search&gsrnamespace=0&gsrlimit={count}&gsrsearch={Uri.EscapeDataString(text)}&prop=pageprops%7Cinfo%7Cdescription&ppprop=wikibase_item&inprop=url"));

    /// <summary>The address that gives the language link in <paramref name="target"/> of the articles <paramref name="pageIds"/> of <paramref name="language"/>'s Wikipedia.</summary>
    public static Uri LinksAddress(string language, IReadOnlyList<long> pageIds, string target) => new(string.Create(CultureInfo.InvariantCulture,
        $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&maxlag=5&prop=langlinks&lllang={Uri.EscapeDataString(target)}&lllimit=max&pageids={string.Join("%7C", pageIds)}"));

    /// <summary>The language links of a links answer by page id (any language the answer holds; a bad language code is left out).</summary>
    public static IReadOnlyDictionary<long, IReadOnlyList<ResearchAlternate>> ParseLinks(JsonElement root)
    {
        var result = new Dictionary<long, IReadOnlyList<ResearchAlternate>>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("query", out var query) || query.ValueKind != JsonValueKind.Object
            || !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var page in pages.EnumerateArray())
        {
            if (PageId(page) is { } id && Links(page) is { Count: > 0 } links)
            {
                result[id] = links;
            }
        }

        return result;
    }

    /// <summary>The address of an article of <paramref name="language"/>'s Wikipedia.</summary>
    public static string ArticleAddress(string language, string title) =>
        $"https://{language}.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";

    /// <summary>The hits of a search answer, in Wikipedia's order (the backend's id is filled in later).</summary>
    public static IReadOnlyList<ResearchHit> ParseSearch(JsonElement root, string language) => Search(root, language).Select(p => p.Hit).ToList();

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<ResearchHit>> SearchCoreAsync(ResearchQuery query, CancellationToken cancellationToken)
    {
        var language = ResearchText.Language(query.Language) ?? "en";
        var response = await Http.GetAsync(SearchAddress(language, query.Text, query.MaxHits), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<(long? PageId, ResearchHit Hit)> found;
        using (var json = Json(response.Body))
        {
            found = Search(json.RootElement, language);
        }

        var ids = found.Select(f => f.PageId).OfType<long>().Distinct().ToList();
        var links = new Dictionary<long, List<ResearchAlternate>>();
        foreach (var target in query.LinkLanguages.Select(ResearchText.Language).OfType<string>().Where(t => t != language).Distinct(StringComparer.Ordinal))
        {
            if (ids.Count == 0)
            {
                break;
            }

            try
            {
                var answer = await Http.GetAsync(LinksAddress(language, ids, target), cancellationToken).ConfigureAwait(false);
                using var json = Json(answer.Body);
                foreach (var (id, alternates) in ParseLinks(json.RootElement))
                {
                    (links.TryGetValue(id, out var list) ? list : (links[id] = [])).AddRange(alternates.Where(a => a.Language == target));
                }
            }
            catch (DocumentRejectedException ex)
            {
                // The hits stay; only this language's versions are not offered (the reason is in the log).
                Logger.LogWarning("Research: Wikipedia ({Language}) language links in {Target} could not be read ({Code})", language, target, ex.Code);
            }
        }

        return found.Select(f => f.PageId is { } id && links.TryGetValue(id, out var more)
            ? f.Hit with { Alternates = [.. f.Hit.Alternates, .. more.Where(m => f.Hit.Alternates.All(a => a.Language != m.Language))] }
            : f.Hit).ToList();
    }

    /// <inheritdoc />
    protected override Task<FetchedDocument> FetchCoreAsync(ResearchHit hit, CancellationToken cancellationToken) =>
        fetcher.FetchAsync(hit.Url, hit.Language, cancellationToken);

    private static long? PageId(JsonElement page) =>
        page.ValueKind == JsonValueKind.Object && page.TryGetProperty("pageid", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) ? number : null;

    /// <summary>The language links a page carries (a bad language code is left out).</summary>
    private static List<ResearchAlternate> Links(JsonElement page)
    {
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

        return links;
    }

    /// <summary>The found articles with their page ids, in Wikipedia's order.</summary>
    private static IReadOnlyList<(long? PageId, ResearchHit Hit)> Search(JsonElement root, string language)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("query", out var query) || query.ValueKind != JsonValueKind.Object
            || !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var hits = new List<(int Index, long? PageId, ResearchHit Hit)>();
        foreach (var page in pages.EnumerateArray())
        {
            var title = Text(page, "title");
            if (title.Length == 0 || page.TryGetProperty("missing", out _))
            {
                continue;
            }

            var index = page.TryGetProperty("index", out var at) && at.ValueKind == JsonValueKind.Number && at.TryGetInt32(out var i) ? i : int.MaxValue;
            var item = page.TryGetProperty("pageprops", out var props) ? Text(props, "wikibase_item") : string.Empty;
            var url = Text(page, "fullurl");
            hits.Add((index, PageId(page), new ResearchHit(string.Empty, title, url.Length > 0 ? url : ArticleAddress(language, title), language)
            {
                Snippet = Text(page, "description"),
                EntityKey = item.Length > 0 ? item : string.Empty,
                Alternates = Links(page),
            }));
        }

        return hits.OrderBy(h => h.Index).Select(h => (h.PageId, h.Hit)).ToList();
    }
}
