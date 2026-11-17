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
        request.UserAgent.ShouldContain("+https://github.com/TM-AutoClip/tm-concept", Case.Sensitive, "the project page, as the Wikimedia policy asks");
        request.Uri.Query.ShouldContain("maxlag=5");
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

    [Theory]
    [InlineData("http://127.0.0.1:7100/admin")]
    [InlineData("http://localhost/")]
    [InlineData("192.168.1.1")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://router/")]
    [InlineData("http://printer.local/")]
    public async Task AnAddressOnThisComputerOrTheLocalNetworkIsRefusedBeforeAnyRequest(string address)
    {
        var handler = new SavedAnswers().Add(HttpStatusCode.OK, "text/html", "<p>router admin</p>");
        var fetcher = new WebPageFetcher(new DocumentTransport(() => handler), NullLogger<WebPageFetcher>.Instance);

        (await Should.ThrowAsync<DocumentRejectedException>(() => fetcher.FetchAsync(address, string.Empty, None))).Code.ShouldBe("private");

        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ARedirectToALocalAddressIsRefusedAndAPublicOneIsFollowed()
    {
        var handler = new SavedAnswers()
            .Redirect(HttpStatusCode.Found, "/moved")
            .Add(HttpStatusCode.OK, "text/html", "<p>Moved here.</p>")
            .Redirect(HttpStatusCode.MovedPermanently, "http://192.168.0.1/login");
        var fetcher = new WebPageFetcher(new DocumentTransport(() => handler), NullLogger<WebPageFetcher>.Instance);

        var page = await fetcher.FetchAsync("https://example.org/old", string.Empty, None);
        page.Url.ShouldBe("https://example.org/moved");
        Encoding.UTF8.GetString(page.Content).ShouldContain("Moved here.");

        (await Should.ThrowAsync<DocumentRejectedException>(() => fetcher.FetchAsync("https://example.org/trap", string.Empty, None))).Code.ShouldBe("private");
        handler.Requests.Select(r => r.Uri.ToString()).ShouldBe(["https://example.org/old", "https://example.org/moved", "https://example.org/trap"],
            "the local address is never asked");
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("100.64.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::ffff:192.168.1.1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::", false)]
    [InlineData("93.184.216.34", true)]
    [InlineData("2606:4700::1111", true)]
    public void OnlyPublicAddressesAreConnectedTo(string address, bool reachable) =>
        NetworkAddresses.IsPublic(IPAddress.Parse(address)).ShouldBe(reachable);

    [Fact]
    public async Task ABodyThatTricklesIsCutOffAtTheTimeLimit()
    {
        var fetcher = new WebPageFetcher(new DocumentTransport(() => new TricklingAnswer()) { TimeLimit = TimeSpan.FromMilliseconds(300) },
            NullLogger<WebPageFetcher>.Instance);

        var error = await Should.ThrowAsync<DocumentRejectedException>(() => fetcher.FetchAsync("https://example.org/slow", string.Empty, None));

        (error.Code, error.Detail).ShouldBe(("network", "0.3 s"));
    }

    [Fact]
    public async Task APageSoBrokenThatReadingItRunsOutOfTimeIsRefusedWithAReason()
    {
        // Thousands of unclosed script tags: every pattern that looks for their end scans the rest of the page.
        var html = "<html><body>" + string.Concat(Enumerable.Repeat("<script>x ", 400_000));
        var handler = new SavedAnswers().Add(HttpStatusCode.OK, "text/html", html);
        var fetcher = new WebPageFetcher(new DocumentTransport(() => handler), NullLogger<WebPageFetcher>.Instance);

        var error = await Should.ThrowAsync<DocumentRejectedException>(() => fetcher.FetchAsync("https://example.org/broken", string.Empty, None));

        error.Code.ShouldBe("not_text");
        error.InnerException.ShouldBeOfType<System.Text.RegularExpressions.RegexMatchTimeoutException>();
    }

    [Fact]
    public async Task AnOlderTurkishPageInWindows1254KeepsItsLetters()
    {
        var body = TMConcept.Documents.Extractors.LegacyEncodings.Named("windows-1254")!.GetBytes("<html><body><p>Ağaç, şişe, ılık, İstanbul, Göğüs.</p></body></html>");
        var handler = new SavedAnswers().Add(HttpStatusCode.OK, "text/html; charset=windows-1254", body)
            .Add(HttpStatusCode.OK, "text/html", TMConcept.Documents.Extractors.LegacyEncodings.Named("iso-8859-9")!
                .GetBytes("<html><head><meta charset=\"iso-8859-9\"></head><body><p>Şeker ılık.</p></body></html>"));
        var fetcher = new WebPageFetcher(new DocumentTransport(() => handler), NullLogger<WebPageFetcher>.Instance);

        Encoding.UTF8.GetString((await fetcher.FetchAsync("https://example.org/eski", string.Empty, None)).Content).ShouldContain("Ağaç, şişe, ılık, İstanbul, Göğüs.");
        Encoding.UTF8.GetString((await fetcher.FetchAsync("https://example.org/meta", string.Empty, None)).Content).ShouldContain("Şeker ılık.");
    }

    private static ExtractedDocument MarkdownExtractorOf(string text) => TMConcept.Documents.Extractors.MarkdownExtractor.Parse(text);

    /// <summary>Sends its headers at once and then its body a byte at a time, slower than any limit.</summary>
    [Fact]
    public async Task AWaitAnswerIsTriedOnceMoreAfterTheAskedWaitAndALongOrSecondOneIsARateError()
    {
        var page = "<html><body><p>The page.</p></body></html>";
        var once = new FixtureHttp().Answer(HttpStatusCode.TooManyRequests, "text/plain", "slow down", ("Retry-After", "0"))
            .Answer(HttpStatusCode.OK, "text/html", page);
        (await Web(once).FetchAsync("https://wait-once.example/a", string.Empty, None)).Title.ShouldBe("wait-once.example");
        once.Requests.Count.ShouldBe(2);

        var twice = new FixtureHttp().Answer(HttpStatusCode.TooManyRequests, "text/plain", "slow", ("Retry-After", "0"))
            .Answer(HttpStatusCode.TooManyRequests, "text/plain", "slow", ("Retry-After", "0"));
        var second = await Should.ThrowAsync<DocumentRejectedException>(() => Web(twice).FetchAsync("https://wait-twice.example/a", string.Empty, None));
        (second.Code, twice.Requests.Count).ShouldBe(("rate", 2));

        var longWait = new FixtureHttp().Answer(HttpStatusCode.TooManyRequests, "text/plain", "slow", ("Retry-After", "3600"));
        var refused = await Should.ThrowAsync<DocumentRejectedException>(() => Web(longWait).FetchAsync("https://wait-long.example/a", string.Empty, None));
        (refused.Code, longWait.Requests.Count).ShouldBe(("rate", 1), "a wait longer than the limit is reported at once");
    }

    [Fact]
    public async Task AWikimediaMaxlagAnswerIsTriedOnceMore()
    {
        var http = new FixtureHttp()
            .Json("""{"error":{"code":"maxlag","info":"Waiting for a database server: 6 seconds lagged."}}""", HttpStatusCode.OK, ("Retry-After", "0"), ("X-Database-Lag", "6"))
            .Json(WikipediaAnswer);
        var fetcher = new WikipediaFetcher(new DocumentTransport(() => http), NullLogger<WikipediaFetcher>.Instance);

        var article = await fetcher.FetchAsync("Bizans", "tr", None);

        article.Title.ShouldBe("Bizans İmparatorluğu");
        http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task OneHostGetsOneRequestAtATimeAcrossTheApp()
    {
        var handler = new CountingAnswers();
        var transport = new DocumentTransport(() => handler);
        var fetchers = Enumerable.Range(0, 3).Select(_ => new WebPageFetcher(transport, NullLogger<WebPageFetcher>.Instance)).ToList();

        await Task.WhenAll(fetchers.SelectMany((f, i) => new[]
        {
            f.FetchAsync("https://one-host.example/" + i, string.Empty, None),
            f.FetchAsync("https://other-host.example/" + i, string.Empty, None),
        }));

        handler.Most.ShouldBe(1, "three fetchers, still one request per host at a time");
        handler.Total.ShouldBe(6);
    }

    private static WebPageFetcher Web(FixtureHttp http) => new(new DocumentTransport(() => http), NullLogger<WebPageFetcher>.Instance);

    /// <summary>Answers every request with a page after a short wait and counts how many requests to one host overlap.</summary>
    private sealed class CountingAnswers : HttpMessageHandler
    {
        private readonly Dictionary<string, int> _running = new(StringComparer.Ordinal);
        private int _most;
        private int _total;

        public int Most => Volatile.Read(ref _most);

        public int Total => Volatile.Read(ref _total);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri!.Host;
            lock (_running)
            {
                _running[host] = _running.GetValueOrDefault(host) + 1;
                _most = Math.Max(_most, _running[host]);
            }

            await Task.Delay(40, cancellationToken);
            lock (_running)
            {
                _running[host]--;
            }

            Interlocked.Increment(ref _total);
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes("<html><body><p>Page.</p></body></html>"));
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/html");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request };
        }
    }

    private sealed class TricklingAnswer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new TricklingStream());
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/html");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }

    /// <summary>A body that gives one byte every ten seconds.</summary>
    private sealed class TricklingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("asynchronous only");

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            buffer.Span[0] = (byte)'x';
            return 1;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Answers requests in order from saved answers and records them (address and User-Agent).</summary>
    private sealed class SavedAnswers : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Type, byte[] Body, string? Location)> _answers = new();

        public List<(Uri Uri, string UserAgent)> Requests { get; } = [];

        public HttpRequestException? Failure { get; init; }

        public SavedAnswers Add(HttpStatusCode status, string type, string body) => Add(status, type, Encoding.UTF8.GetBytes(body));

        public SavedAnswers Add(HttpStatusCode status, string type, byte[] body)
        {
            _answers.Enqueue((status, type, body, null));
            return this;
        }

        public SavedAnswers Redirect(HttpStatusCode status, string location)
        {
            _answers.Enqueue((status, "text/html", [], location));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.UserAgent.ToString()));
            if (Failure is not null)
            {
                throw Failure;
            }

            var (status, type, body, location) = _answers.Dequeue();
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(type);
            var response = new HttpResponseMessage(status) { Content = content, RequestMessage = request };
            if (location is not null)
            {
                response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            }

            return Task.FromResult(response);
        }
    }
}
