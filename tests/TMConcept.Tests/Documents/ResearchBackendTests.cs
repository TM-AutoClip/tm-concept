using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Documents;
using TMConcept.Core.Keys;
using TMConcept.Core.Research;
using TMConcept.Documents.Research;
using TMConcept.Documents.Web;

namespace TMConcept.Tests.Documents;

/// <summary>The research backends from saved answers (a fake HTTP handler; nothing reaches the network, no key is real).</summary>
public sealed class ResearchBackendTests
{
    private const string WikipediaSearch = """
        {"batchcomplete":true,"query":{"pages":[
          {"pageid":2,"ns":0,"title":"Varang Muhafızları","index":2,"description":"Bizans imparatorlarının muhafız birliği",
           "pageprops":{"wikibase_item":"Q838185"},"fullurl":"https://tr.wikipedia.org/wiki/Varang_Muhaf%C4%B1zlar%C4%B1",
           "langlinks":[{"lang":"el","title":"Βαράγγειος Φρουρά"},{"lang":"en","title":"Varangian Guard"},{"lang":"bad code!","title":"x"}]},
          {"pageid":1,"ns":0,"title":"Bizans İmparatorluğu","index":1,"pageprops":{"wikibase_item":"Q12544"},
           "fullurl":"https://tr.wikipedia.org/wiki/Bizans_%C4%B0mparatorlu%C4%9Fu"},
          {"ns":0,"title":"Yok","missing":true}]}}
        """;

    private const string WikidataSearch = """
        {"search":[{"id":"Q838185","title":"Q838185","label":"Varangian Guard","description":"elite unit of the Byzantine army"},
                   {"id":"P31","label":"instance of"},{"id":"Q1","label":""}],"success":1}
        """;

    private const string WikidataFacts = """
        {"head":{"vars":["propLabel","value","valueLabel"]},"results":{"bindings":[
          {"propLabel":{"type":"literal","value":"instance of"},"value":{"type":"uri","value":"http://www.wikidata.org/entity/Q1076486"},"valueLabel":{"type":"literal","value":"military unit"}},
          {"propLabel":{"type":"literal","value":"inception"},"value":{"type":"literal","value":"0988-01-01T00:00:00Z"},"valueLabel":{"type":"literal","value":"0988-01-01T00:00:00Z"}},
          {"propLabel":{"type":"literal","value":"dissolved"},"value":{"type":"literal","value":"1204-04-13T00:00:00Z"}},
          {"propLabel":{"type":"literal","value":"country"},"value":{"type":"uri","value":"http://www.wikidata.org/entity/Q12544"},"valueLabel":{"type":"literal","value":"Byzantine Empire"}},
          {"propLabel":{"type":"literal","value":"country"},"value":{"type":"uri","value":"http://www.wikidata.org/entity/Q99999"},"valueLabel":{"type":"literal","value":"Q99999"}},
          {"propLabel":{"type":"literal","value":"P9999"},"value":{"type":"literal","value":"x"}}]}}
        """;

    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task WikipediaSearchesOnceAndGroupsTheArticleByItsWikidataItemWithItsOtherLanguages()
    {
        var http = new FixtureHttp().Json(WikipediaSearch);
        var backend = Wikipedia(http);

        var hits = await backend.SearchAsync(new ResearchQuery("Varang muhafızları", "tr", 5), None);

        var request = http.Requests.ShouldHaveSingleItem();
        request.Uri.Host.ShouldBe("tr.wikipedia.org");
        request.Uri.Query.ShouldContain("generator=search");
        request.Uri.Query.ShouldContain("gsrsearch=Varang%20muhaf%C4%B1zlar%C4%B1");
        request.Uri.Query.ShouldContain("langlinks");
        request.Uri.Query.ShouldContain("wikibase_item");
        request.Headers["User-Agent"].ShouldStartWith("TM-Concept/");
        hits.Select(h => h.Title).ShouldBe(["Bizans İmparatorluğu", "Varang Muhafızları"], "Wikipedia's own order, the missing page left out");
        var guard = hits[1];
        (guard.BackendId, guard.Rank, guard.EntityKey, guard.Language).ShouldBe(("wikipedia", 1, "Q838185", "tr"));
        guard.Snippet.ShouldBe("Bizans imparatorlarının muhafız birliği");
        guard.Alternates.Select(a => a.Language).ShouldBe(["el", "en"], "a bad language code is dropped");
        guard.Alternates[0].Url.ShouldBe("https://el.wikipedia.org/wiki/" + Uri.EscapeDataString("Βαράγγειος_Φρουρά"));
        WikipediaFetcher.Article(guard.Alternates[0].Url, string.Empty).ShouldBe(("el", "Βαράγγειος Φρουρά"), "the Greek version is fetched by its own title");
    }

    [Fact]
    public async Task WikidataFindsItemsAndKeepsTheirFactsAsAShortPage()
    {
        var http = new FixtureHttp().Json(WikidataSearch).Json(WikidataFacts);
        var backend = new WikidataResearchBackend(new DocumentTransport(() => http), NullLogger<WikidataResearchBackend>.Instance);

        var hit = (await backend.SearchAsync(new ResearchQuery("Varangian Guard", "en", 5), None)).ShouldHaveSingleItem();
        (hit.EntityKey, hit.Url, hit.Snippet).ShouldBe(("wikidata:Q838185", "https://www.wikidata.org/wiki/Q838185", "elite unit of the Byzantine army"));
        http.Requests[0].Uri.Query.ShouldContain("action=wbsearchentities");
        http.Requests[0].Uri.Query.ShouldContain("language=en");

        var page = await backend.FetchAsync(hit, None);

        page.Kind.ShouldBe(DocumentKind.Wikidata);
        Uri.UnescapeDataString(http.Requests[1].Uri.Query).ShouldContain("wd:Q838185");
        Uri.UnescapeDataString(http.Requests[1].Uri.Query).ShouldContain("wikibase:language \"en,en\"");
        var text = Encoding.UTF8.GetString(page.Content);
        text.ShouldStartWith("# Varangian Guard\n\nelite unit of the Byzantine army\n");
        text.ShouldContain("- instance of: military unit\n");
        text.ShouldContain("- inception: 0988\n", Case.Sensitive, "1 January at midnight is a year");
        text.ShouldContain("- dissolved: 1204-04-13\n");
        text.ShouldContain("- country: Byzantine Empire\n", Case.Sensitive, "an item without a label is left out");
        text.ShouldNotContain("P9999");
    }

    [Theory]
    [InlineData("Q42 } . ?s ?p ?o")]
    [InlineData("Q0")]
    [InlineData("P31")]
    [InlineData("")]
    public void OnlyAnItemIdGoesIntoTheFactsQuery(string item) => WikidataResearchBackend.FactsQuery(item, "en").ShouldBeNull();

    [Fact]
    public void ALanguageThatIsNotACodeDoesNotGoIntoTheFactsQuery() => WikidataResearchBackend.FactsQuery("Q42", "en\". }").ShouldBeNull();

    [Fact]
    public async Task TavilySendsTheKeyOnlyInItsHeaderAndNeverLogsIt()
    {
        const string secret = "tvly-TESTONLY-0000000000000000";
        var http = new FixtureHttp().Json("""
            {"query":"x","results":[{"title":"The Varangians","url":"https://example.org/varangians","content":"Norse guards","score":0.9},
                                    {"title":"No address","url":"javascript:alert(1)"}]}
            """);
        var log = new RecordingLogger<TavilyResearchBackend>();
        var backend = new TavilyResearchBackend(new DocumentTransport(() => http), new MemoryKeys(secret), Web(http), log);

        var hits = await backend.SearchAsync(new ResearchQuery("Varangian Guard", "tr", 5), None);

        var request = http.Requests.ShouldHaveSingleItem();
        (request.Method, request.Uri.ToString()).ShouldBe(("POST", "https://api.tavily.com/search"));
        request.Headers["Authorization"].ShouldBe("Bearer " + secret);
        request.Body.ShouldNotContain(secret);
        JsonDocument.Parse(request.Body).RootElement.GetProperty("max_results").GetInt32().ShouldBe(5);
        hits.ShouldHaveSingleItem().Url.ShouldBe("https://example.org/varangians");
        log.All.ShouldNotContain(secret);
        log.All.ShouldNotContain("tvly-");
    }

    [Fact]
    public async Task ARefusedTavilyKeyIsAKeyErrorAndNothingIsLogged()
    {
        const string secret = "tvly-TESTONLY-refused-000000000";
        var http = new FixtureHttp().Json("""{"detail":{"error":"Unauthorized"}}""", HttpStatusCode.Unauthorized);
        var log = new RecordingLogger<TavilyResearchBackend>();
        var backend = new TavilyResearchBackend(new DocumentTransport(() => http), new MemoryKeys(secret), Web(http), log);

        (await Should.ThrowAsync<DocumentRejectedException>(() => backend.SearchAsync(new ResearchQuery("x", "en", 5), None))).Code.ShouldBe("key");

        log.All.ShouldNotContain(secret);
        var none = new TavilyResearchBackend(new DocumentTransport(() => http), new MemoryKeys(string.Empty), Web(http), log);
        (await Should.ThrowAsync<DocumentRejectedException>(() => none.SearchAsync(new ResearchQuery("x", "en", 5), None))).Code.ShouldBe("key");
        http.Requests.Count.ShouldBe(1, "without a key nothing is sent");
    }

    [Fact]
    public async Task TheUsersAddressesAreHitsWithoutAnyRequestAndALocalOneIsRefusedWhenFetched()
    {
        var http = new FixtureHttp();
        var backend = new UrlResearchBackend(Web(http), new WikipediaFetcher(new DocumentTransport(() => http), NullLogger<WikipediaFetcher>.Instance));

        var hits = await backend.SearchAsync(new ResearchQuery(string.Empty, "tr", 10)
        {
            Urls = ["https://de.wikipedia.org/wiki/Warägergarde", "example.org/guard/", "https://example.org/guard?utm_source=x", "not an address at all", "http://192.168.1.1/admin"],
        }, None);

        http.Requests.ShouldBeEmpty();
        hits.Select(h => (h.Title, h.Language)).ShouldBe([("Warägergarde", "de"), ("example.org/guard", string.Empty), ("192.168.1.1/admin", string.Empty)],
            "the same page twice is one hit");
        (await Should.ThrowAsync<DocumentRejectedException>(() => backend.FetchAsync(hits[2], None))).Code.ShouldBe("private");
        http.Requests.ShouldBeEmpty("the local address is never asked");
    }

    [Fact]
    public async Task AFetchedPageLosesTheCharactersThatHideText()
    {
        var http = new FixtureHttp().Answer(HttpStatusCode.OK, "text/html; charset=utf-8",
            "<html><body><p>Visible​ text‮ reversed⁦ isolated﻿ and­ soft.</p><p>فارسی‌ها</p></body></html>");
        var backend = new UrlResearchBackend(Web(http), new WikipediaFetcher(new DocumentTransport(() => http), NullLogger<WikipediaFetcher>.Instance));
        var hit = (await backend.SearchAsync(new ResearchQuery(string.Empty, "en", 1) { Urls = ["https://example.org/page"] }, None)).Single();

        var text = Encoding.UTF8.GetString((await backend.FetchAsync(hit, None)).Content);

        text.ShouldContain("Visible text reversed isolated and soft.");
        text.ShouldContain("فارسی‌ها", Case.Sensitive, "the zero-width non-joiner belongs to Persian words");
    }

    [Fact]
    public async Task AHitOfAnotherBackendIsRefused()
    {
        var http = new FixtureHttp();
        var backend = Wikipedia(http);
        await Should.ThrowAsync<ArgumentException>(() => backend.FetchAsync(new ResearchHit("wikidata", "x", "https://www.wikidata.org/wiki/Q1", "en"), None));
    }

    private static WikipediaResearchBackend Wikipedia(FixtureHttp http) => new(new DocumentTransport(() => http),
        new WikipediaFetcher(new DocumentTransport(() => http), NullLogger<WikipediaFetcher>.Instance), NullLogger<WikipediaResearchBackend>.Instance);

    private static WebPageFetcher Web(FixtureHttp http) => new(new DocumentTransport(() => http), NullLogger<WebPageFetcher>.Instance);

    /// <summary>A key store with one Tavily key (or none).</summary>
    private sealed class MemoryKeys(string secret) : IApiKeyStore
    {
        public Task<IReadOnlyList<ApiKey>> ListAsync(string? provider, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApiKey>>(secret.Length == 0 || provider != TavilyResearchBackend.KeyProvider
                ? []
                : [new ApiKey { Provider = TavilyResearchBackend.KeyProvider, Secret = secret }]);

        public Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<ApiKey?>(null);

        public Task SaveAsync(ApiKey key, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkOkAsync(Guid id, DateTimeOffset atUtc, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
