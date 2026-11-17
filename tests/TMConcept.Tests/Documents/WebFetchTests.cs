using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Documents;
using TMConcept.Documents.Web;

namespace TMConcept.Tests.Documents;

/// <summary>Web pages and Wikipedia articles from saved answers (a fake HTTP handler; nothing reaches the network).</summary>
public sealed class WebFetchTests
{
    private const string WikipediaAnswer = """
        {"batchcomplete":true,"query":{"redirects":[{"from":"Bizans","to":"Bizans İmparatorluğu"}],"pages":[{"pageid":42,"ns":0,"title":"Bizans İmparatorluğu",
        "contentmodel":"wikitext","pagelanguage":"tr","fullurl":"https://tr.wikipedia.org/wiki/Bizans_%C4%B0mparatorlu%C4%9Fu",
        "extract":"Bizans İmparatorluğu, Doğu Roma'dır.\n\n\n== Tarih ==\nKonstantinopolis 330'da kuruldu.\n\n=== Erken dönem ===\nJustinianus.\n\n== Ekonomi ==\nİpek."}]}}
        """;

    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData("https://tr.wikipedia.org/wiki/Bizans_%C4%B0mparatorlu%C4%9Fu", "en", "tr", "Bizans İmparatorluğu")]
    [InlineData("https://en.m.wikipedia.org/wiki/Hagia_Sophia", "tr", "en", "Hagia Sophia")]
    [InlineData("de.wikipedia.org/wiki/Seidenstraße", "tr", "de", "Seidenstraße")]
    [InlineData("Ayasofya", "TR", "tr", "Ayasofya")]
    [InlineData("Rome", "zh-yue", "zh-yue", "Rome")]
    public void AnArticleIsFoundByItsAddressOrByItsTitleAndLanguage(string address, string language, string expectedLanguage, string expectedTitle) =>
        WikipediaFetcher.Article(address, language).ShouldBe((expectedLanguage, expectedTitle));

    [Theory]
    [InlineData("https://example.org/wiki/Rome", "en")]
    [InlineData("https://tr.wikipedia.org/w/index.php?title=Rome", "en")]
    [InlineData("Rome", "not a language")]
    [InlineData("", "en")]
    public void SomethingElseIsNotAnArticle(string address, string language) => WikipediaFetcher.Article(address, language).ShouldBeNull();

    [Fact]
    public async Task AnArticleComesFromOneApiCallWithItsHeadingsAndAGenericUserAgent()
    {
        var handler = new SavedAnswers().Add(HttpStatusCode.OK, "application/json; charset=utf-8", WikipediaAnswer);
        var fetcher = new WikipediaFetcher(new DocumentTransport(() => handler), NullLogger<WikipediaFetcher>.Instance);

        var article = await fetcher.FetchAsync("Bizans", "tr", None);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.Host.ShouldBe("tr.wikipedia.org");
        request.Uri.AbsolutePath.ShouldBe("/w/api.php");
        request.Uri.Query.ShouldContain("titles=Bizans");
        request.Uri.Query.ShouldContain("explaintext=1");
        request.UserAgent.ShouldStartWith("TM-Concept/");
        request.UserAgent.ShouldNotContain("@", Case.Sensitive, "nothing about the user");
        (article.Kind, article.Title, article.Language, article.Extension).ShouldBe((DocumentKind.Wikipedia, "Bizans İmparatorluğu", "tr", ".md"));
        article.Url.ShouldBe("https://tr.wikipedia.org/wiki/Bizans_%C4%B0mparatorlu%C4%9Fu");
        var markdown = Encoding.UTF8.GetString(article.Content);
        markdown.ShouldStartWith("# Bizans İmparatorluğu\n");
        markdown.ShouldContain("\n## Tarih\n");
        markdown.ShouldContain("\n### Erken dönem\n");
    }

    [Fact]
    public async Task AMissingArticleIsNotFound()
    {
        var handler = new SavedAnswers().Add(HttpStatusCode.OK, "application/json", """{"query":{"pages":[{"ns":0,"title":"Yok böyle","missing":true}]}}""");
        var fetcher = new WikipediaFetcher(new DocumentTransport(() => handler), NullLogger<WikipediaFetcher>.Instance);
        (await Should.ThrowAsync<DocumentRejectedException>(() => fetcher.FetchAsync("Yok böyle", "tr", None))).Code.ShouldBe("not_found");
    }

    [Fact]
    public async Task AWebPageBecomesItsReadableTextWithoutScripts()
    {
        const string html = """
            <html><head><title>Ignored title</title><meta property="og:title" content="The Silk Road &amp; Byzantium">
            <script>alert('x'); document.write('<p>never</p>')</script><style>p{color:red}</style></head>
            <body><nav><a href="/">Home</a> Menu</nav><header>Site header</header>
            <article><h1>The Silk Road</h1><p>Caravans carried <b>silk</b>&nbsp;to Constantinople.</p><!-- hidden --><ul><li>Pepper</li><li>Glass</li></ul>
            <h2>Taxes</h2><p>The emperor taxed it.</p><form>Subscribe</form></article>
            <aside>Related links</aside><footer>Copyright</footer></body></html>
            """;
        var handler = new SavedAnswers().Add(HttpStatusCode.OK, "text/html; charset=utf-8", html);
        var fetcher = new WebPageFetcher(new DocumentTransport(() => handler), NullLogger<WebPageFetcher>.Instance);

        var page = await fetcher.FetchAsync("example.org/silk", string.Empty, None);

        handler.Requests.ShouldHaveSingleItem().Uri.ToString().ShouldBe("https://example.org/silk");
        page.Title.ShouldBe("The Silk Road & Byzantium");
        page.Extension.ShouldBe(".md");
        var text = Encoding.UTF8.GetString(page.Content);
        text.ShouldContain("# The Silk Road");
        text.ShouldContain("Caravans carried silk to Constantinople.");
        text.ShouldContain("- Pepper");
        text.ShouldContain("## Taxes");
        foreach (var gone in new[] { "alert", "never", "color:red", "Menu", "Site header", "Related links", "Copyright", "hidden", "Subscribe" })
        {
            text.ShouldNotContain(gone, Case.Sensitive);
        }

        var document = MarkdownExtractorOf(text);
        document.Sections.Select(s => s.Heading).ShouldContain("The Silk Road › Taxes");
    }

    [Fact]
    public async Task APdfAddressKeepsThePdfAndOtherTypesAreRefused()
    {
        var handler = new SavedAnswers()
            .Add(HttpStatusCode.OK, "application/pdf", "%PDF-1.7")
            .Add(HttpStatusCode.OK, "image/png", "png")
            .Add(HttpStatusCode.NotFound, "text/html", "no")
            .Add(HttpStatusCode.OK, "text/html", new string('x', (int)DocumentLimits.WebBytesMax + 10));
        var fetcher = new WebPageFetcher(new DocumentTransport(() => handler), NullLogger<WebPageFetcher>.Instance);

        var pdf = await fetcher.FetchAsync("https://example.org/files/report.pdf", string.Empty, None);
        (pdf.Extension, pdf.Title).ShouldBe((".pdf", "report"));
        (await Should.ThrowAsync<DocumentRejectedException>(() => fetcher.FetchAsync("https://example.org/a.png", string.Empty, None))).Code.ShouldBe("not_text");
        (await Should.ThrowAsync<DocumentRejectedException>(() => fetcher.FetchAsync("https://example.org/gone", string.Empty, None))).Code.ShouldBe("not_found");
        (await Should.ThrowAsync<DocumentRejectedException>(() => fetcher.FetchAsync("https://example.org/big", string.Empty, None))).Code.ShouldBe("size");
        (await Should.ThrowAsync<DocumentRejectedException>(() => fetcher.FetchAsync("ftp://example.org/x", string.Empty, None))).Code.ShouldBe("address");
    }

    [Fact]
    public async Task AnUnreachableAddressSaysTheSystemsReason()
    {
        var fetcher = new WebPageFetcher(new DocumentTransport(() => new SavedAnswers { Failure = new HttpRequestException("No such host is known.") }),
            NullLogger<WebPageFetcher>.Instance);
        var error = await Should.ThrowAsync<DocumentRejectedException>(() => fetcher.FetchAsync("https://nowhere.invalid", string.Empty, None));
        (error.Code, error.Detail).ShouldBe(("network", "No such host is known."));
    }

    private static ExtractedDocument MarkdownExtractorOf(string text) => TMConcept.Documents.Extractors.MarkdownExtractor.Parse(text);

    /// <summary>Answers requests in order from saved answers and records them (address and User-Agent).</summary>
    private sealed class SavedAnswers : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Type, byte[] Body)> _answers = new();

        public List<(Uri Uri, string UserAgent)> Requests { get; } = [];

        public HttpRequestException? Failure { get; init; }

        public SavedAnswers Add(HttpStatusCode status, string type, string body)
        {
            _answers.Enqueue((status, type, Encoding.UTF8.GetBytes(body)));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.UserAgent.ToString()));
            if (Failure is not null)
            {
                throw Failure;
            }

            var (status, type, body) = _answers.Dequeue();
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(type);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content, RequestMessage = request });
        }
    }
}
