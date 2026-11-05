using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Anthropic;
using TMConcept.Providers.Catalog;
using TMConcept.Providers.Common;
using TMConcept.Providers.Fal;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Tests.Providers;

public sealed class ProviderTests
{
    private const string Key = "fake-test-key-0123456789abcdefghij";

    private const string Stream = """
        event: message_start
        data: {"type":"message_start","message":{"usage":{"input_tokens":120,"cache_read_input_tokens":3000,"cache_creation_input_tokens":0,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"{\"konseptler\":"}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"[]}"}}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":42}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    private static readonly EmbeddedModelCatalog Catalog = new();

    private static ProviderHttp Http(FakeHttp fake) => new(fake.Transport, NullLogger<ProviderHttp>.Instance);

    [Fact]
    public async Task AnthropicSendsTheKeyOnlyToItsOwnAddressAndParsesTheStream()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, Stream, "text/event-stream");
        var provider = new AnthropicProvider(Http(fake), NullLogger<AnthropicProvider>.Instance);
        var client = provider.CreateChat(Catalog.Find("anthropic", "claude-sonnet-5-5")!, Key);

        var answer = await client.SendAsync(new ChatRequest("system text", "user text", CacheSystem: true, Schema: new JsonObject { ["type"] = "object" }, Effort: "medium"), CancellationToken.None);

        answer.Text.ShouldBe("{\"konseptler\":[]}");
        answer.Usage.ShouldBe(new ChatUsage(120, 3000, 0, 42));
        var request = fake.Requests.Single();
        request.Uri.ShouldBe(new Uri("https://api.anthropic.com/v1/messages"));
        request.Headers["x-api-key"].ShouldBe(Key);
        request.Headers["anthropic-version"].ShouldBe(AnthropicProvider.ApiVersion);
        var body = JsonNode.Parse(request.Body)!;
        body["system"]![0]!["cache_control"]!["type"]!.ToString().ShouldBe("ephemeral");
        body["output_config"]!["format"]!["type"]!.ToString().ShouldBe("json_schema");
        body["output_config"]!["effort"]!.ToString().ShouldBe("medium");
        body["temperature"].ShouldBeNull();
        body["stream"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task ACutOffAnswerIsAnErrorThatKeepsTheBilledUsage()
    {
        var cut = Stream.Replace("end_turn", "max_tokens", StringComparison.Ordinal);
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, cut, "text/event-stream");
        var provider = new AnthropicProvider(Http(fake), NullLogger<AnthropicProvider>.Instance);
        var client = provider.CreateChat(Catalog.Find("anthropic", "claude-haiku-5-5")!, Key);

        var error = await Should.ThrowAsync<ProviderException>(() => client.SendAsync(new ChatRequest("s", "u"), CancellationToken.None));

        error.Code.ShouldBe("truncated");
        error.ChargedUsage!.Output.ShouldBe(42);
    }

    [Fact]
    public async Task A429IsRetriedOnceAfterTheWaitTheProviderAsks()
    {
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.TooManyRequests, """{"error":{"message":"slow down"}}""", edit: r => r.Headers.RetryAfter = new(TimeSpan.FromSeconds(3)))
            .Answer(HttpStatusCode.OK, """{"data":[]}""");
        var provider = new OpenAIProvider(Http(fake), NullLogger<OpenAIProvider>.Instance);

        var result = await provider.TestAsync(Key, CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.Requests.Count.ShouldBe(2);
        fake.Delays.ShouldBe([TimeSpan.FromSeconds(3)]);
    }

    [Fact]
    public async Task ASecond429IsNotRetried()
    {
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.TooManyRequests, "{}")
            .Answer(HttpStatusCode.TooManyRequests, "{}");
        var provider = new OpenAIProvider(Http(fake), NullLogger<OpenAIProvider>.Instance);

        var result = await provider.TestAsync(Key, CancellationToken.None);

        result.Ok.ShouldBeFalse();
        result.Code.ShouldBe("rate");
        fake.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AFailureNeverCarriesTheKey()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.Unauthorized, $$$"""{"error":{"message":"invalid x-api-key {{{Key}}}"}}""");
        var provider = new AnthropicProvider(Http(fake), NullLogger<AnthropicProvider>.Instance);

        var result = await provider.TestAsync(Key, CancellationToken.None);

        result.Ok.ShouldBeFalse();
        result.Code.ShouldBe("auth");
        result.Detail.ShouldNotContain(Key);
        result.Detail.ShouldContain("invalid x-api-key");
    }

    [Fact]
    public async Task FalSavesTheKeyUntestedWithoutARequest()
    {
        var fake = new FakeHttp();
        var provider = new FalProvider(Http(fake), NullLogger<FalProvider>.Instance);

        var result = await provider.TestAsync(Key, CancellationToken.None);

        result.Code.ShouldBe("untested");
        fake.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task OpenRouterReportsTheExactCostWhichWinsOverThePriceTable()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """
            {"choices":[{"message":{"content":"{}"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":1000,"completion_tokens":200,"prompt_tokens_details":{"cached_tokens":400},"cost":0.0123}}
            """);
        var provider = new OpenRouterProvider(Http(fake), NullLogger<OpenRouterProvider>.Instance);
        var model = Catalog.All.First(m => m.Provider == "openrouter" && m.Capabilities.HasFlag(ModelCapability.Chat));
        var client = provider.CreateChat(model, Key);

        var answer = await client.SendAsync(new ChatRequest("s", "u"), CancellationToken.None);

        answer.ReportedCostUsd.ShouldBe(0.0123m);
        answer.Usage.ShouldBe(new ChatUsage(600, 400, 0, 200));
        new CostCalculator().Exact(model, answer.Usage, answer.ReportedCostUsd).ShouldBe(0.0123m);
        fake.Requests.Single().Uri.Host.ShouldBe("openrouter.ai");
        fake.Requests.Single().Headers["Authorization"].ShouldBe("Bearer " + Key);
        JsonNode.Parse(fake.Requests.Single().Body)!["usage"]!["include"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void EveryProviderHasItsOwnFixedHttpsAddressAndCatalog()
    {
        var http = Http(new FakeHttp());
        IProvider[] providers =
        [
            new AnthropicProvider(http, NullLogger<AnthropicProvider>.Instance),
            new OpenAIProvider(http, NullLogger<OpenAIProvider>.Instance),
            new OpenRouterProvider(http, NullLogger<OpenRouterProvider>.Instance),
            new ZaiProvider(http, NullLogger<ZaiProvider>.Instance),
            new MistralProvider(http, NullLogger<MistralProvider>.Instance),
            new DeepSeekProvider(http, NullLogger<DeepSeekProvider>.Instance),
            new FalProvider(http, NullLogger<FalProvider>.Instance),
        ];

        providers.Select(p => p.Code).Distinct().Count().ShouldBe(providers.Length);
        foreach (var provider in providers)
        {
            provider.Address.Scheme.ShouldBe("https");
            provider.KeyPage.Scheme.ShouldBe("https");
            Catalog.All.Count(m => m.Provider == provider.Code).ShouldBeGreaterThan(0, provider.Code);
        }
    }

    [Fact]
    public void TheCatalogPricesEveryModel()
    {
        Catalog.All.Count.ShouldBeGreaterThan(10);
        foreach (var model in Catalog.All)
        {
            model.PricedOn.ShouldNotBeNullOrEmpty();
            var priced = model.InputUsd > 0 || model.OutputUsd > 0 || model.ImageUsd > 0 || model.SpeechUsdPerMillionCharacters > 0;
            priced.ShouldBeTrue(model.Provider + "/" + model.Id);
        }
    }

    [Fact]
    public void ExactCostUsesCachePricesAndTheLongContextPrice()
    {
        var costs = new CostCalculator();
        var sonnet = Catalog.Find("anthropic", "claude-sonnet-5-5")!;
        costs.Exact(sonnet, new ChatUsage(1_000_000, 0, 0, 0)).ShouldBe(2.00m);
        costs.Exact(sonnet, new ChatUsage(0, 1_000_000, 0, 100_000)).ShouldBe(0.20m + 1.00m);

        var haiku = Catalog.Find("anthropic", "claude-haiku-5-5")!;
        costs.Exact(haiku, new ChatUsage(50_000, 0, 0, 0)).ShouldBe(0.005m);
        costs.Exact(haiku, new ChatUsage(200_000, 0, 0, 0)).ShouldBe(0.10m);
    }

    [Fact]
    public void APriceOverrideReplacesAPriceAndAddsAMissingModel()
    {
        var catalog = new EmbeddedModelCatalog();
        catalog.UseOverrides(
        [
            new ModelPriceOverride { Provider = "anthropic", ModelId = "claude-sonnet-5-5", InputUsd = 9, OutputUsd = 9 },
            new ModelPriceOverride { Provider = "openrouter", ModelId = "someone/new-model", InputUsd = 1, OutputUsd = 2 },
        ]);

        catalog.Find("anthropic", "claude-sonnet-5-5")!.InputUsd.ShouldBe(9);
        catalog.Find("openrouter", "someone/new-model")!.Capabilities.HasFlag(ModelCapability.Chat).ShouldBeTrue();
        new EmbeddedModelCatalog().Find("anthropic", "claude-sonnet-5-5")!.InputUsd.ShouldBe(2.00m);
    }
}
