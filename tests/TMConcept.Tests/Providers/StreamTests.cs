using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
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
    public async Task EveryAnthropicAnswerWritesOneLineWithTheRawStopReasonTheBudgetAndTheTokensButNeverThePromptOrTheKey()
    {
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.OK, Start + Ending("end_turn", 5000), "text/event-stream")
            .Answer(HttpStatusCode.OK, Start + Ending("model_context_window_exceeded", 7000), "text/event-stream");
        var logger = new ListLogger<AnthropicProvider>();
        var client = new AnthropicProvider(Http(fake.Transport), logger).CreateChat(Catalog.Find("anthropic", "claude-sonnet-5-5")!, Key);

        await client.SendAsync(new ChatRequest("SYSTEM-PROMPT-TEXT", "USER-PROMPT-TEXT", MaxOutputTokens: 64_000, Effort: "high"), CancellationToken.None);
        var cut = await Should.ThrowAsync<ProviderException>(() => client.SendAsync(new ChatRequest("SYSTEM-PROMPT-TEXT", "USER-PROMPT-TEXT",
            MaxOutputTokens: 64_000), CancellationToken.None));

        cut.Code.ShouldBe("truncated");
        cut.StopReason.ShouldBe("model_context_window_exceeded", "the raw stop reason is kept: two of them share the code");
        var lines = logger.Entries.Where(e => e.Message.Contains("stop reason", StringComparison.Ordinal)).ToList();
        lines.Count.ShouldBe(2);
        lines[0].Level.ShouldBe(LogLevel.Information);
        lines[0].Message.ShouldContain("claude-sonnet-5-5: stop reason end_turn, max_tokens 64000, effort high, tokens in 900 / out 5000 / cache read 0 / cache write 4000");
        lines[1].Level.ShouldBe(LogLevel.Warning, "a cut is a warning");
        lines[1].Message.ShouldContain("stop reason model_context_window_exceeded, max_tokens 64000, effort automatic");
        logger.Entries.ShouldAllBe(e => !e.Message.Contains("PROMPT-TEXT", StringComparison.Ordinal) && !e.Message.Contains(Key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOpenAICompatibleAnswerCutAtLengthWritesAWarningLineWithTheFinishReason()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """
            data: {"choices":[{"index":0,"delta":{"content":"[{"},"finish_reason":"length"}],"usage":{"prompt_tokens":10,"completion_tokens":9}}

            data: [DONE]

            """, "text/event-stream");
        var logger = new ListLogger<DeepSeekProvider>();
        var client = new DeepSeekProvider(Http(fake.Transport), logger)
            .CreateChat(Catalog.All.First(m => m.Provider == "deepseek" && m.Capabilities.HasFlag(ModelCapability.Chat)), Key);

        var cut = await Should.ThrowAsync<ProviderException>(() => client.SendAsync(new ChatRequest("SYSTEM-PROMPT-TEXT", "USER-PROMPT-TEXT", Stream: true),
            CancellationToken.None));

        cut.StopReason.ShouldBe("length");
        var line = logger.Entries.Single(e => e.Message.Contains("finish reason", StringComparison.Ordinal));
        line.Level.ShouldBe(LogLevel.Warning);
        line.Message.ShouldContain("finish reason length, max_tokens 16000, effort -, tokens in 10 / out 9 / cache read 0");
        logger.Entries.ShouldAllBe(e => !e.Message.Contains("PROMPT-TEXT", StringComparison.Ordinal) && !e.Message.Contains(Key, StringComparison.Ordinal));
    }

    [Fact]
    public void AnOpenAICompatibleBodyCarriesTheOutputLimitAndTheEffort()
    {
        var http = Http(new FakeHttp().Transport);
        var openai = Catalog.All.First(m => m.Provider == "openai" && m.Id == "gpt-6-luna");
        var openaiBody = ((OpenAICompatibleChatClient)new OpenAIProvider(http, new ListLogger<OpenAIProvider>()).CreateChat(openai, Key))
            .Body(new ChatRequest("S", "U", MaxOutputTokens: 100_000, Effort: "low"));
        openaiBody["max_completion_tokens"]!.GetValue<int>().ShouldBe(64_000, "lowered to the model's own limit");
        openaiBody.ContainsKey("max_tokens").ShouldBeFalse();
        openaiBody["reasoning_effort"]!.GetValue<string>().ShouldBe("low");

        var routed = Catalog.All.First(m => m.Provider == "openrouter" && m.Capabilities.HasFlag(ModelCapability.Chat));
        var routerBody = ((OpenAICompatibleChatClient)new OpenRouterProvider(http, new ListLogger<OpenRouterProvider>()).CreateChat(routed, Key))
            .Body(new ChatRequest("S", "U", MaxOutputTokens: 64_000, Effort: "high"));
        routerBody["max_tokens"]!.GetValue<int>().ShouldBe(64_000);
        routerBody["reasoning"]!["effort"]!.GetValue<string>().ShouldBe("high");
        routerBody.ContainsKey("reasoning_effort").ShouldBeFalse();

        var deepseek = Catalog.All.First(m => m.Provider == "deepseek" && m.Capabilities.HasFlag(ModelCapability.Chat));
        var plain = ((OpenAICompatibleChatClient)new DeepSeekProvider(http, new ListLogger<DeepSeekProvider>()).CreateChat(deepseek, Key))
            .Body(new ChatRequest("S", "U", MaxOutputTokens: 64_000));
        plain["max_tokens"]!.GetValue<int>().ShouldBe(64_000, "no known limit: the request's own");
        plain.ContainsKey("reasoning_effort").ShouldBeFalse("no effort chosen: none sent");
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

    /// <summary>Keeps every entry.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
