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
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Tests.Providers;

/// <summary>Streamed answers (SSE) read line by line: Anthropic and the OpenAI-compatible providers. Fake network only.</summary>
public sealed class StreamTests
{
    private const string Key = "fake-test-key-0123456789abcdefghij";

    private const string Start = """
        event: message_start
        data: {"type":"message_start","message":{"usage":{"input_tokens":900,"cache_read_input_tokens":0,"cache_creation_input_tokens":4000,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"plan the scenes"}}

        event: ping
        data: {"type":"ping"}

        event: content_block_start
        data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"window.__timelines = "}}


        """;

    private static readonly EmbeddedModelCatalog Catalog = new();

    private static ProviderHttp Http(ProviderTransport transport) => new(transport, NullLogger<ProviderHttp>.Instance);

    private static string Ending(string stop, long output) => $$$"""
        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"{};"}}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"{{{stop}}}"},"usage":{"output_tokens":{{{output}}}}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    [Fact]
    public async Task AnthropicReadsTheStreamLineByLineSkipsThinkingAndSendsTheModelBudget()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, Start + Ending("end_turn", 5000), "text/event-stream");
        var client = new AnthropicProvider(Http(fake.Transport), NullLogger<AnthropicProvider>.Instance)
            .CreateChat(Catalog.Find("anthropic", "claude-opus-5-5")!, Key);

        var answer = await client.SendAsync(new ChatRequest("s", "u", MaxOutputTokens: 64_000, Stream: true), CancellationToken.None);

        answer.Text.ShouldBe("window.__timelines = {};");
        answer.Usage.ShouldBe(new ChatUsage(900, 0, 4000, 5000));
        answer.StopReason.ShouldBe("end_turn");
        var body = JsonNode.Parse(fake.Requests.Single().Body)!;
        body["stream"]!.GetValue<bool>().ShouldBeTrue();
        body["max_tokens"]!.GetValue<int>().ShouldBe(64_000);
    }

    [Fact]
    public async Task AnAnthropicAnswerStoppedAtMaxTokensIsTruncatedAndItsUsageIsBilled()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, Start + Ending("max_tokens", 64_000), "text/event-stream");
        var model = Catalog.Find("anthropic", "claude-opus-5-5")!;
        var client = new AnthropicProvider(Http(fake.Transport), NullLogger<AnthropicProvider>.Instance).CreateChat(model, Key);

        var cut = await Should.ThrowAsync<ProviderException>(() => client.SendAsync(new ChatRequest("s", "u", MaxOutputTokens: 64_000), CancellationToken.None));

        cut.Code.ShouldBe("truncated");
        cut.ChargedUsage.ShouldBe(new ChatUsage(900, 0, 4000, 64_000));
        cut.PartialText.ShouldBe("window.__timelines = {};");

        // 900 × 4 $ + 4 000 × 5 $ (cache write) + 64 000 × 20 $ per million tokens.
        new CostCalculator().Exact(model, cut.ChargedUsage!).ShouldBe(0.0036m + 0.02m + 1.28m);
    }

    [Fact]
    public async Task AStreamThatGoesSilentIsATimeoutThatKeepsWhatWasBilled()
    {
        var fake = new FakeHttp().Answer(() =>
        {
            var content = new StreamContent(new StallingStream(Start));
            content.Headers.ContentType = new("text/event-stream");
            return content;
        });
        var transport = fake.Transport with { StreamIdle = TimeSpan.FromMilliseconds(200) };
        var client = new AnthropicProvider(Http(transport), NullLogger<AnthropicProvider>.Instance).CreateChat(Catalog.Find("anthropic", "claude-sonnet-5-5")!, Key);

        var stopped = await Should.ThrowAsync<ProviderException>(() => client.SendAsync(new ChatRequest("s", "u"), CancellationToken.None));

        stopped.Code.ShouldBe("timeout");
        stopped.ChargedUsage!.CacheWrite.ShouldBe(4000);
        stopped.PartialText.ShouldBe("window.__timelines = ");
    }

    [Fact]
    public async Task ACanceledStreamIsACancelNotATimeout()
    {
        var fake = new FakeHttp().Answer(() => new StreamContent(new StallingStream(Start)));
        var client = new AnthropicProvider(Http(fake.Transport), NullLogger<AnthropicProvider>.Instance).CreateChat(Catalog.Find("anthropic", "claude-sonnet-5-5")!, Key);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(() => client.SendAsync(new ChatRequest("s", "u"), cancel.Token));
    }

    [Fact]
    public void TheWholeBodyParserStillReadsAPlainJsonAnswer()
    {
        var answer = AnthropicStream.Parse("""
            {"content":[{"type":"thinking","thinking":"x"},{"type":"text","text":"done"}],"stop_reason":"end_turn",
             "usage":{"input_tokens":10,"output_tokens":3}}
            """);

        answer.Text.ShouldBe("done");
        answer.Streamed.ShouldBeFalse();
        answer.Usage.ShouldBe(new ChatUsage(10, 0, 0, 3));
    }

    [Fact]
    public async Task AnOpenAICompatibleLongCallStreamsAndReadsTheUsageOfTheLastChunk()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """
            : OPENROUTER PROCESSING

            data: {"choices":[{"index":0,"delta":{"role":"assistant","content":"const a"},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"content":" = 1;"},"finish_reason":"stop"}]}

            data: {"choices":[],"usage":{"prompt_tokens":1000,"completion_tokens":300,"prompt_tokens_details":{"cached_tokens":200},"cost":0.004}}

            data: [DONE]

            """, "text/event-stream");
        var model = Catalog.All.First(m => m.Provider == "openrouter" && m.Capabilities.HasFlag(ModelCapability.Chat));
        var client = new OpenRouterProvider(Http(fake.Transport), NullLogger<OpenRouterProvider>.Instance).CreateChat(model, Key);

        var answer = await client.SendAsync(new ChatRequest("s", "u", Stream: true), CancellationToken.None);

        answer.Text.ShouldBe("const a = 1;");
        answer.StopReason.ShouldBe("stop");
        answer.Usage.ShouldBe(new ChatUsage(800, 200, 0, 300));
        answer.ReportedCostUsd.ShouldBe(0.004m);
        var body = JsonNode.Parse(fake.Requests.Single().Body)!;
        body["stream"]!.GetValue<bool>().ShouldBeTrue();
        body["stream_options"]!["include_usage"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task AShortOpenAICompatibleCallIsNotStreamed()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":1}}""");
        var client = new OpenAIProvider(Http(fake.Transport), NullLogger<OpenAIProvider>.Instance)
            .CreateChat(Catalog.All.First(m => m.Provider == "openai" && m.Capabilities.HasFlag(ModelCapability.Chat)), Key);

        (await client.SendAsync(new ChatRequest("s", "u"), CancellationToken.None)).Text.ShouldBe("ok");

        var body = JsonNode.Parse(fake.Requests.Single().Body)!;
        body["stream"]!.GetValue<bool>().ShouldBeFalse();
        body["stream_options"].ShouldBeNull();
    }

    [Fact]
    public async Task AStreamCutAtLengthOrWithoutAFinishReasonIsAnErrorWithItsUsage()
    {
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.OK, """
                data: {"choices":[{"index":0,"delta":{"content":"[{"},"finish_reason":"length"}],"usage":{"prompt_tokens":10,"completion_tokens":9}}

                data: [DONE]

                """, "text/event-stream")
            .Answer(HttpStatusCode.OK, """
                data: {"choices":[{"index":0,"delta":{"content":"half"},"finish_reason":null}],"usage":{"prompt_tokens":10,"completion_tokens":4}}

                """, "text/event-stream");
        var client = new DeepSeekProvider(Http(fake.Transport), NullLogger<DeepSeekProvider>.Instance)
            .CreateChat(Catalog.All.First(m => m.Provider == "deepseek" && m.Capabilities.HasFlag(ModelCapability.Chat)), Key);

        var cut = await Should.ThrowAsync<ProviderException>(() => client.SendAsync(new ChatRequest("s", "u", Stream: true), CancellationToken.None));
        cut.Code.ShouldBe("truncated");
        cut.PartialText.ShouldBe("[{");
        cut.ChargedUsage!.Output.ShouldBe(9);

        var early = await Should.ThrowAsync<ProviderException>(() => client.SendAsync(new ChatRequest("s", "u", Stream: true), CancellationToken.None));
        early.Code.ShouldBe("incomplete");
        early.PartialText.ShouldBe("half");
        early.ChargedUsage!.Output.ShouldBe(4);
    }

    [Fact]
    public async Task AProviderThatRejectsStreamOptionsGetsTheRequestOnceMoreWithoutThem()
    {
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.UnprocessableEntity, """{"message":"Extra inputs are not permitted: stream_options"}""")
            .Answer(HttpStatusCode.OK, """
                data: {"choices":[{"index":0,"delta":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":1}}

                data: [DONE]

                """, "text/event-stream");
        var client = new MistralProvider(Http(fake.Transport), NullLogger<MistralProvider>.Instance)
            .CreateChat(Catalog.All.First(m => m.Provider == "mistral" && m.Capabilities.HasFlag(ModelCapability.Chat)), Key);

        var answer = await client.SendAsync(new ChatRequest("s", "u", Stream: true), CancellationToken.None);

        answer.Text.ShouldBe("ok");
        fake.Requests.Count.ShouldBe(2);
        JsonNode.Parse(fake.Requests[1].Body)!["stream_options"].ShouldBeNull();
        JsonNode.Parse(fake.Requests[1].Body)!["stream"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task AnErrorObjectInsideTheStreamIsAFailure()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """
            data: {"choices":[{"index":0,"delta":{"content":"par"},"finish_reason":null}]}

            data: {"error":{"code":502,"message":"upstream died"}}

            """, "text/event-stream");
        var client = new OpenRouterProvider(Http(fake.Transport), NullLogger<OpenRouterProvider>.Instance)
            .CreateChat(Catalog.All.First(m => m.Provider == "openrouter" && m.Capabilities.HasFlag(ModelCapability.Chat)), Key);

        var failed = await Should.ThrowAsync<ProviderException>(() => client.SendAsync(new ChatRequest("s", "u", Stream: true), CancellationToken.None));

        failed.Code.ShouldBe("busy");
        failed.Message.ShouldContain("upstream died");
    }
}
