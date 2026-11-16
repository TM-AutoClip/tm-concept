using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Anthropic;
using TMConcept.Providers.Catalog;
using TMConcept.Providers.Chat;
using TMConcept.Providers.Common;
using TMConcept.Providers.Endpoints;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Tests.Providers;

/// <summary>
/// User-added local servers (OpenAI-compatible) against a fake server on 127.0.0.1: the model list, plain and streamed answers, thinking
/// fields and blocks, a missing usage, 401, a 503 while the model loads, the json_schema fallback, the catalog merge and the client factory.
/// </summary>
public sealed class EndpointTests
{
    private const string Token = "local-test-token-0123456789";
    private static readonly CancellationToken None = CancellationToken.None;

    private static (EndpointRegistry Registry, EndpointChatTransport Transport) Create(FakeEndpointServer server)
    {
        var registry = new EndpointRegistry(new MemoryEndpointStore(), NullLogger<EndpointRegistry>.Instance);
        return (registry, new EndpointChatTransport(registry, server.Transport, NullLoggerFactory.Instance));
    }

    private static LocalEndpoint Endpoint(FakeEndpointServer server, string token = "", bool schema = false, int maxOutput = 4096) => new()
    {
        Name = "Lemonade",
        BaseUrl = server.BaseUrl,
        Token = token,
        Models = [new EndpointModel { Id = "bonsai-27b", ContextTokens = 32768, MaxOutputTokens = maxOutput, JsonSchema = schema }],
    };

    private static async Task<IChatClient> ChatAsync(FakeEndpointServer server, LocalEndpoint endpoint)
    {
        var (registry, transport) = Create(server);
        await registry.SaveAsync(endpoint, None);
        var model = EndpointCodes.Catalog(endpoint)[0];
        return transport.CreateChat(model, endpoint.Token);
    }

    [Fact]
    public async Task TheModelListGivesIdsAndContextAndTheTokenOnlyWhenSet()
    {
        using var server = new FakeEndpointServer("api/v1");
        server.Models = """
            {"object":"list","data":[
              {"id":"Bonsai-27B-GGUF","max_context_window":262144,"context_length":16384},
              {"id":"vllm-model","max_model_len":8192},
              {"id":"llama","meta":{"n_ctx_train":131072}},
              {"id":"plain"}, {"id":""}, {"id":"plain"}]}
            """;
        var (_, probe) = Create(server);

        var listing = await probe.ListModelsAsync(server.BaseUrl + "/", Token, None);
        var anonymous = await probe.ListModelsAsync(server.BaseUrl, string.Empty, None);

        listing.Ok.ShouldBeTrue();
        listing.Models.ShouldBe([new ListedModel("Bonsai-27B-GGUF", 16384), new ListedModel("vllm-model", 8192), new ListedModel("llama", 0),
            new ListedModel("plain", 0)], "llama.cpp's n_ctx_train is the training context, not the one the server runs");
        anonymous.Models.Count.ShouldBe(4);
        var requests = server.Requests.ToList();
        requests[0].Uri.AbsolutePath.ShouldBe("/api/v1/models", "the /api/v1 part of the address is kept");
        requests[0].Headers["Authorization"].ShouldBe("Bearer " + Token);
        requests[1].Headers.ContainsKey("Authorization").ShouldBeFalse("no token, no header");
    }

    [Fact]
    public async Task ALlamaServerWithOneModelGivesTheContextItRunsWithFromItsProps()
    {
        using var server = new FakeEndpointServer();
        server.Models = """{"object":"list","data":[{"id":"qwen3-8b","meta":{"n_ctx_train":131072}}]}""";
        server.Props = """{"default_generation_settings":{"n_ctx":8192},"total_slots":1}""";
        var (_, probe) = Create(server);

        var listing = await probe.ListModelsAsync(server.BaseUrl, Token, None);

        listing.Models.ShouldBe([new ListedModel("qwen3-8b", 8192)]);
        var props = server.Requests.Single(r => r.Uri.AbsolutePath == "/props");
        props.Headers["Authorization"].ShouldBe("Bearer " + Token);

        // Without the page (Lemonade, Ollama) the context stays unknown and the editor's default applies; the list still works.
        server.Props = null;
        (await probe.ListModelsAsync(server.BaseUrl, string.Empty, None)).Models.ShouldBe([new ListedModel("qwen3-8b", 0)]);
    }

    [Theory]
    [InlineData(0, 4096)]
    [InlineData(8192, 4096)]
    [InlineData(16384, 8192)]
    [InlineData(32768, 24576)]
    [InlineData(131072, 64000)]
    [InlineData(2048, 1024)]
    public void TheDefaultAnswerLimitIsTheContextLessRoomForThePrompt(long context, int expected) =>
        EndpointCodes.DefaultMaxOutput(context).ShouldBe(expected);

    [Fact]
    public async Task TheAnswerLimitFitsTheContextAndAPromptThatCannotFitIsNotSent()
    {
        using var server = new FakeEndpointServer();
        server.Chat(200, FakeEndpointServer.Plain("{}"));
        var client = await ChatAsync(server, Endpoint(server, maxOutput: 64_000));
        var request = new ChatRequest("system", new string('x', 60_000), MaxOutputTokens: 64_000);

        await client.SendAsync(request, None);

        var sent = JsonNode.Parse(server.Chats.Single().Body)!["max_tokens"]!.GetValue<int>();
        sent.ShouldBe((int)(32768 - OpenAICompatibleChatClient.PromptTokens(request)), "a 32 768 context leaves this much next to a 20 000-token prompt");

        var tooLarge = await Should.ThrowAsync<ProviderException>(() => client.SendAsync(new ChatRequest("system", new string('x', 100_000)), None));
        tooLarge.Code.ShouldBe("too_large");
        server.Chats.Count.ShouldBe(1, "a prompt larger than the context is not processed for minutes only to be rejected");
    }

    [Theory]
    [InlineData("http://localhost:13305/api/v1", true, false)]
    [InlineData("http://user:pass@localhost:8080/v1", false, true)]
    [InlineData("http://user@localhost:8080/v1", false, true)]
    [InlineData("localhost:8080", false, false)]
    public void AnAddressWithAUserOrPasswordIsRefusedWithItsOwnReason(string text, bool valid, bool credentials)
    {
        EndpointAddress.TryParse(text, out _).ShouldBe(valid);
        EndpointAddress.HasCredentials(text).ShouldBe(credentials);
    }

    [Fact]
    public async Task ModelListFailuresHaveTheirOwnCodes()
    {
        using var server = new FakeEndpointServer();
        server.ModelList(401, """{"error":{"message":"invalid api key local-test-token-0123456789"}}""")
            .ModelList(200, """{"object":"list","data":[]}""")
            .ModelList(200, "<html>not an api</html>", "text/html")
            .ModelList(404, "not found", "text/plain");
        var (_, probe) = Create(server);

        var refused = await probe.ListModelsAsync(server.BaseUrl, Token, None);
        (await probe.ListModelsAsync(server.BaseUrl, string.Empty, None)).Code.ShouldBe("empty");
        (await probe.ListModelsAsync(server.BaseUrl, string.Empty, None)).Code.ShouldBe("unreadable");
        (await probe.ListModelsAsync(server.BaseUrl, string.Empty, None)).Code.ShouldBe("model");
        (await probe.ListModelsAsync("localhost:13305", string.Empty, None)).Code.ShouldBe("address");
        (await probe.ListModelsAsync("ftp://localhost/v1", string.Empty, None)).Code.ShouldBe("address");

        refused.Ok.ShouldBeFalse();
        refused.Code.ShouldBe("auth");
        refused.Detail.ShouldNotContain(Token, Case.Sensitive, "the token is masked in the detail");
    }

    [Fact]
    public async Task AServerThatIsNotRunningIsANetworkFailure()
    {
        string address;
        using (var gone = new FakeEndpointServer())
        {
            address = gone.BaseUrl;
        }

        using var server = new FakeEndpointServer();
        var (_, probe) = Create(server);

        var listing = await probe.ListModelsAsync(address, string.Empty, None);

        listing.Ok.ShouldBeFalse();
        listing.Code.ShouldBe("network");
    }

    [Fact]
    public async Task APlainAnswerLosesItsThinkBlockAndAMissingUsageCostsNothing()
    {
        using var server = new FakeEndpointServer();
        server.Chat(200, FakeEndpointServer.Plain("<think>The user wants {json}. Let me plan.</think>\n{\"topics\":[\"Greek fire\"]}"));
        var endpoint = Endpoint(server, maxOutput: 2048);
        var client = await ChatAsync(server, endpoint);

        var answer = await client.SendAsync(new ChatRequest("system", "user", Schema: new JsonObject { ["type"] = "object" }, MaxOutputTokens: 64_000), None);

        answer.Text.ShouldBe("{\"topics\":[\"Greek fire\"]}");
        answer.Usage.ShouldBe(new ChatUsage(0, 0, 0, 0));
        new CostCalculator().Exact(client.Model, answer.Usage, answer.ReportedCostUsd).ShouldBe(0m);
        var request = server.Chats.Single();
        request.Uri.AbsolutePath.ShouldBe("/v1/chat/completions");
        request.Headers.ContainsKey("Authorization").ShouldBeFalse();
        var body = JsonNode.Parse(request.Body)!;
        body["model"]!.ToString().ShouldBe("bonsai-27b");
        body["max_tokens"]!.GetValue<int>().ShouldBe(2048, "the step asked 64 000; the model's own limit is sent");
        body["max_completion_tokens"].ShouldBeNull();
        body["response_format"]!["type"]!.ToString().ShouldBe("json_object", "json_schema only for a model marked so");
        body["reasoning_effort"].ShouldBeNull();
    }

    [Fact]
    public async Task AStreamCountsReasoningAsThinkingKeepsOnlyTheContentAndWarnsNotAboutUsage()
    {
        using var server = new FakeEndpointServer();
        server.Chat(200, FakeEndpointServer.Stream("{\"topics\":[\"Iconoclasm\"]}"), "text/event-stream");
        var endpoint = Endpoint(server, Token);
        var (registry, transport) = Create(server);
        await registry.SaveAsync(endpoint, None);
        var logger = new EntryLogger();
        var http = new ProviderHttp(server.Transport.Http, NullLogger<ProviderHttp>.Instance);
        var client = new EndpointProvider(endpoint, endpoint.Models[0], server.Transport, http, logger).CreateChat(EndpointCodes.Catalog(endpoint)[0], Token);
        var steps = new List<ChatProgress>();

        var answer = await client.SendAsync(new ChatRequest("s", "u", Stream: true, Progress: new ForwardProgress<ChatProgress>(steps.Add)), None);

        answer.Text.ShouldBe("{\"topics\":[\"Iconoclasm\"]}");
        answer.StopReason.ShouldBe("stop");
        steps.Select(s => s.Phase).Distinct().ShouldBe([ChatPhase.Sent, ChatPhase.Thinking, ChatPhase.Writing, ChatPhase.Done]);
        steps.Where(s => s.Phase == ChatPhase.Writing).ShouldAllBe(s => !s.Text.Contains("think", StringComparison.OrdinalIgnoreCase));
        logger.Entries.ShouldNotContain(e => e.Contains("counted as 0", StringComparison.Ordinal), "a local call is free: no usage is no warning");
        logger.Entries.ShouldNotContain(e => e.Contains(Token, StringComparison.Ordinal));
        server.Chats.Single().Headers["Authorization"].ShouldBe("Bearer " + Token);
        JsonNode.Parse(server.Chats.Single().Body)!["stream"]!.GetValue<bool>().ShouldBeTrue();
        transport.Dispose();
        http.Dispose();
    }

    [Fact]
    public async Task UnauthorizedIsAnAuthFailure()
    {
        using var server = new FakeEndpointServer();
        server.Chat(401, """{"error":{"message":"Invalid API key"}}""");
        var client = await ChatAsync(server, Endpoint(server, "wrong-token-0123456789"));

        var error = await Should.ThrowAsync<ProviderException>(() => client.SendAsync(new ChatRequest("s", "u"), None));

        error.Code.ShouldBe("auth");
        server.Chats.Count.ShouldBe(1, "a 401 is not retried");
    }

    [Fact]
    public async Task A503WhileTheModelLoadsIsRetriedWithAGrowingWait()
    {
        using var server = new FakeEndpointServer();
        server.Chat(503, """{"error":{"message":"Loading model"}}""")
            .Chat(503, """{"error":{"message":"Loading model"}}""")
            .Chat(200, FakeEndpointServer.Plain("{\"ok\":true}"));
        var client = await ChatAsync(server, Endpoint(server));

        var answer = await client.SendAsync(new ChatRequest("s", "u"), None);

        answer.Text.ShouldBe("{\"ok\":true}");
        server.Chats.Count.ShouldBe(3);
        server.Delays.ShouldBe([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)]);
    }

    [Fact]
    public async Task A503ThatLastsFailsAfterTheRetries()
    {
        using var server = new FakeEndpointServer();
        for (var i = 0; i < 6; i++)
        {
            server.Chat(503, """{"error":{"message":"Loading model"}}""");
        }

        var client = await ChatAsync(server, Endpoint(server));

        var error = await Should.ThrowAsync<ProviderException>(() => client.SendAsync(new ChatRequest("s", "u"), None));

        error.Code.ShouldBe("busy");
        server.Chats.Count.ShouldBe(5, "the first try and four retries");
    }

    [Fact]
    public async Task ARejectedJsonSchemaIsSentOnceMoreWithoutTheOptionalFields()
    {
        using var server = new FakeEndpointServer();
        server.Chat(400, """{"error":{"message":"response_format json_schema is not supported"}}""")
            .Chat(200, FakeEndpointServer.Plain("{\"a\":1}"));
        var client = await ChatAsync(server, Endpoint(server, schema: true));

        var answer = await client.SendAsync(new ChatRequest("s", "u", Schema: new JsonObject { ["type"] = "object" }), None);

        answer.Text.ShouldBe("{\"a\":1}");
        var chats = server.Chats;
        chats.Count.ShouldBe(2);
        JsonNode.Parse(chats[0].Body)!["response_format"]!["type"]!.ToString().ShouldBe("json_schema");
        JsonNode.Parse(chats[1].Body)!.AsObject().ContainsKey("response_format").ShouldBeFalse();
    }

    [Theory]
    [InlineData("<think>plan {a}</think>{\"x\":1}", "{\"x\":1}")]
    [InlineData("<think>a</think>\n<think>b</think>\n answer ", "answer")]
    [InlineData("reasoning without the opening tag</think>{\"x\":2}", "{\"x\":2}")]
    [InlineData("{\"x\":3}<think>cut off while thinking", "{\"x\":3}")]
    [InlineData("{\"text\":\"no tags\"}", "{\"text\":\"no tags\"}")]
    public void ThinkBlocksAreRemoved(string text, string expected) => ThinkBlocks.Strip(text).ShouldBe(expected);

    [Fact]
    public async Task TheCatalogMergesEndpointModelsFreeAndForgetsADeletedServer()
    {
        var registry = new EndpointRegistry(new MemoryEndpointStore(), NullLogger<EndpointRegistry>.Instance);
        var embedded = new EmbeddedModelCatalog();
        var catalog = new MergedModelCatalog(embedded, registry);
        var before = catalog.All.Count;
        var endpoint = new LocalEndpoint
        {
            Name = "LM Studio",
            BaseUrl = "http://localhost:1234/v1",
            Models =
            [
                new EndpointModel { Id = "qwen3-vl", Vision = true, JsonSchema = true },
                new EndpointModel { Id = "gemma", Name = "Gemma", ContextTokens = 16384, MaxOutputTokens = 8000 },
            ],
        };

        await registry.SaveAsync(endpoint, None);

        var code = EndpointCodes.For(endpoint.Id);
        catalog.All.Count.ShouldBe(before + 2);
        var vision = catalog.Find(code, "qwen3-vl")!;
        vision.Name.ShouldBe("qwen3-vl · LM Studio");
        (vision.InputUsd, vision.OutputUsd).ShouldBe((0m, 0m));
        vision.Capabilities.ShouldBe(ModelCapability.Chat | ModelCapability.Vision | ModelCapability.Schema);
        (vision.ContextTokens, vision.MaxOutputTokens).ShouldBe((EndpointDefaults.ContextTokens, EndpointDefaults.MaxOutputTokens));
        vision.Efforts.ShouldBeEmpty();
        catalog.Find(code, "gemma")!.Name.ShouldBe("Gemma · LM Studio");
        catalog.With(ModelCapability.Chat).Count(m => m.Provider == code).ShouldBe(2);
        catalog.With(ModelCapability.Vision).Single(m => m.Provider == code).Id.ShouldBe("qwen3-vl");
        catalog.BuiltIn(code, "gemma").ShouldBeNull("endpoint models have no built-in price to be checked against");
        catalog.Find("anthropic", "claude-sonnet-5-5").ShouldNotBeNull();
        catalog.Prices.ShouldBe(embedded.Prices);

        await registry.DeleteAsync(endpoint.Id, None);

        catalog.Find(code, "gemma").ShouldBeNull("a saved step choice of a deleted server shows as no longer offered");
        catalog.All.Count.ShouldBe(before);
    }

    [Fact]
    public async Task TheFactoryTakesAnEmptyKeyOnlyForALocalServer()
    {
        using var server = new FakeEndpointServer();
        var endpoint = Endpoint(server);
        var (registry, transport) = Create(server);
        await registry.SaveAsync(endpoint, None);
        var http = new ProviderHttp(new OfflineHttp().Transport, NullLogger<ProviderHttp>.Instance);
        var factory = new ChatClientFactory([new AnthropicProvider(http, NullLogger<AnthropicProvider>.Instance), transport]);
        var model = EndpointCodes.Catalog(endpoint)[0];

        factory.Create(model, string.Empty).Model.ShouldBe(model);
        Should.Throw<ArgumentException>(() => factory.Create(new EmbeddedModelCatalog().Find("anthropic", "claude-sonnet-5-5")!, string.Empty));
        Should.Throw<InvalidOperationException>(() => factory.Create(new CatalogModel { Provider = EndpointCodes.For(Guid.NewGuid()), Id = "x" }, string.Empty));
        EndpointCodes.Kind(model.Provider).ShouldBe(ModelProviders.Endpoint);
        EndpointCodes.Kind("anthropic").ShouldBe("anthropic");
        transport.Dispose();
        http.Dispose();
    }

    /// <summary>Keeps every log line.</summary>
    private sealed class EntryLogger : ILogger
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            lock (Entries)
            {
                Entries.Add(formatter(state, exception));
            }
        }
    }
}
