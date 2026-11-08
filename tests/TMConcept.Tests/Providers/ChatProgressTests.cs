using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Models;
using TMConcept.Providers.Anthropic;
using TMConcept.Providers.Catalog;
using TMConcept.Providers.Common;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Tests.Providers;

/// <summary>The steps of a chat call for the screen (sent → thinking → writing → done), from a fake stream. Fake network only.</summary>
public sealed class ChatProgressTests
{
    private const string Key = "fake-test-key-0123456789abcdefghij";

    private static readonly EmbeddedModelCatalog Catalog = new();

    private static ProviderHttp Http(ProviderTransport transport) => new(transport, NullLogger<ProviderHttp>.Instance);

    /// <summary>The phases in order, a phase reported again in a row counted once.</summary>
    private static List<ChatPhase> Phases(IEnumerable<ChatProgress> steps)
    {
        var phases = new List<ChatPhase>();
        foreach (var step in steps)
        {
            if (phases.Count == 0 || phases[^1] != step.Phase)
            {
                phases.Add(step.Phase);
            }
        }

        return phases;
    }

    [Fact]
    public async Task AnAnthropicCallIsSentThenThinksThenWritesThenIsDone()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """
            event: message_start
            data: {"type":"message_start","message":{"usage":{"input_tokens":900,"output_tokens":1}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"plan the topics"}}

            event: content_block_start
            data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"{\"topics\": [\"a\""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"]}"}}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":40}}

            """, "text/event-stream");
        var client = new AnthropicProvider(Http(fake.Transport), NullLogger<AnthropicProvider>.Instance)
            .CreateChat(Catalog.Find("anthropic", "claude-haiku-5-5")!, Key);
        var steps = new List<ChatProgress>();

        await client.SendAsync(new ChatRequest("s", "u", Progress: new ForwardProgress<ChatProgress>(steps.Add)), CancellationToken.None);

        Phases(steps).ShouldBe([ChatPhase.Sent, ChatPhase.Thinking, ChatPhase.Writing, ChatPhase.Done]);
        steps.First(s => s.Phase == ChatPhase.Writing).Text.ShouldStartWith("{\"topics\"", Case.Sensitive, "only the visible text, never the thinking");
        steps[^1].Text.ShouldBe("{\"topics\": [\"a\"]}");
        steps[^1].ApproxTokens.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task AStreamedOpenAICompatibleCallReportsTheSameSteps()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """
            : OPENROUTER PROCESSING

            data: {"choices":[{"index":0,"delta":{"role":"assistant","content":"const a"},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"content":" = 1;"},"finish_reason":"stop"}]}

            data: [DONE]

            """, "text/event-stream");
        var model = Catalog.All.First(m => m.Provider == "openrouter" && m.Capabilities.HasFlag(ModelCapability.Chat));
        var client = new OpenRouterProvider(Http(fake.Transport), NullLogger<OpenRouterProvider>.Instance).CreateChat(model, Key);
        var steps = new List<ChatProgress>();

        await client.SendAsync(new ChatRequest("s", "u", Stream: true, Progress: new ForwardProgress<ChatProgress>(steps.Add)), CancellationToken.None);

        Phases(steps).ShouldBe([ChatPhase.Sent, ChatPhase.Thinking, ChatPhase.Writing, ChatPhase.Done]);
        steps[^1].Text.ShouldBe("const a = 1;");
    }

    [Fact]
    public async Task AShortCallThatIsNotStreamedIsSentThenDone()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":1}}""");
        var client = new OpenAIProvider(Http(fake.Transport), NullLogger<OpenAIProvider>.Instance)
            .CreateChat(Catalog.All.First(m => m.Provider == "openai" && m.Capabilities.HasFlag(ModelCapability.Chat)), Key);
        var steps = new List<ChatProgress>();

        await client.SendAsync(new ChatRequest("s", "u", Progress: new ForwardProgress<ChatProgress>(steps.Add)), CancellationToken.None);

        Phases(steps).ShouldBe([ChatPhase.Sent, ChatPhase.Done]);
    }

    [Fact]
    public void WritingIsReportedAtMostFourTimesASecondButANewPhaseAtOnce()
    {
        var clock = new FakeTimeProvider();
        var steps = new List<ChatProgress>();
        var reporter = new ChatProgressReporter(new ForwardProgress<ChatProgress>(steps.Add), clock);

        reporter.Sent();
        clock.Advance(TimeSpan.FromSeconds(3));
        reporter.Line(0, () => string.Empty);
        reporter.Line(0, () => string.Empty);
        reporter.Line(4, () => "abcd");
        clock.Advance(TimeSpan.FromMilliseconds(100));
        reporter.Line(8, () => "abcdefgh");
        clock.Advance(TimeSpan.FromMilliseconds(200));
        reporter.Line(12, () => "abcdefghijkl");
        reporter.Done("abcdefghijkl");

        steps.Select(s => s.Phase).ShouldBe([ChatPhase.Sent, ChatPhase.Thinking, ChatPhase.Writing, ChatPhase.Writing, ChatPhase.Done],
            "the thinking lines once; the writing at 100 ms is skipped, the one at 300 ms is shown");
        steps[1].Elapsed.ShouldBe(TimeSpan.FromSeconds(3));
        steps[3].ApproxTokens.ShouldBe(3);
    }

    [Fact]
    public void WithoutAProgressNothingIsReported()
    {
        var reporter = new ChatProgressReporter(null, new FakeTimeProvider());
        reporter.Sent();
        reporter.Line(3, () => throw new InvalidOperationException("the text is not read without a progress"));
        reporter.Done("x");
    }

    [Fact]
    public void TheCompleteConceptsOfAnAnswerStillArrivingAreCounted()
    {
        ConceptResponseParser.CompleteCount("""{"konseptler":[{"baslik":"a","icerik":"x {y}"},{"baslik":"b","ic""").ShouldBe(1);
        ConceptResponseParser.CompleteCount("""{"konseptler":[""").ShouldBe(0);
        ConceptResponseParser.CompleteCount(string.Empty).ShouldBe(0);
        ConceptResponseParser.CompleteCount(FakeChat.Json(["one", "two", "three"])).ShouldBe(3);
    }
}
