using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Endpoints;
using TMConcept.Tests.Core;

namespace TMConcept.Tests.Providers;

/// <summary>
/// Ollama and LM Studio through their own APIs, against fake servers on 127.0.0.1: found by their address (also when typed with /v1) and on
/// this computer, their text models listed with context, images, thinking and size; Ollama's native chat (context always sent, schema as
/// format, think, keep_alive, a stream, a cut answer, a refused format); LM Studio loaded with the model's context before a chat; both
/// unloaded and confirmed gone, also by the graphics card coordinator when another local model takes the card; memory in use read.
/// </summary>
public sealed class LocalServerTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly JsonObject Schema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["topics"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } } },
    };

    private static (EndpointRegistry Registry, EndpointChatTransport Transport) Create(params string[] detect)
    {
        var registry = new EndpointRegistry(new MemoryEndpointStore(), NullLogger<EndpointRegistry>.Instance);
        return (registry, new EndpointChatTransport(registry, FakeLocalServer.Transport(detect), NullLoggerFactory.Instance));
    }

    private static LocalEndpoint Ollama(FakeOllamaServer server, bool schema = true, bool canThink = false, long context = 16384) => new()
    {
        Name = "Ollama",
        BaseUrl = server.Root,
        Kind = EndpointKind.Ollama,
        Models = [new EndpointModel { Id = "qwen3:8b", ContextTokens = context, MaxOutputTokens = 8192, JsonSchema = schema, CanThink = canThink }],
    };

    private static LocalEndpoint LmStudio(FakeLmStudioServer server, long context = 16384) => new()
    {
        Name = "LM Studio",
        BaseUrl = server.Root,
        Kind = EndpointKind.LmStudio,
        Models = [new EndpointModel { Id = "qwen/qwen3-8b", ContextTokens = context, MaxOutputTokens = 8192, JsonSchema = true }],
    };

    private static async Task<(IChatClient Chat, EndpointChatTransport Transport)> ChatAsync(LocalEndpoint endpoint)
    {
        var (registry, transport) = Create();
        await registry.SaveAsync(endpoint, None);
        return (transport.CreateChat(EndpointCodes.Catalog(endpoint)[0], string.Empty), transport);
    }

    [Fact]
    public async Task OllamaIsFoundAtItsAddressAlsoWithV1AndListsItsTextModels()
    {
        using var server = new FakeOllamaServer();
        var (_, probe) = Create();

        var listing = await probe.ListModelsAsync(server.Root + "/v1", string.Empty, None);

        listing.Ok.ShouldBeTrue();
        listing.Kind.ShouldBe(EndpointKind.Ollama);
        listing.Address.ShouldBe(server.Root, "Ollama is kept by its own address, without /v1");
        listing.Version.ShouldBe("0.40.16");
        listing.Models.Select(m => m.Id).ShouldBe(["qwen3:8b", "llama3.2:latest", "gemma3:4b"], ignoreOrder: true,
            "the embedding-only and the cloud model are left out");
        var qwen = listing.Models.Single(m => m.Id == "qwen3:8b");
        (qwen.ContextTokens, qwen.CanThink, qwen.Vision, qwen.JsonSchema, qwen.SizeBytes).ShouldBe((EndpointDefaults.ServerContextTokens, true, false, true, 5_225_376_047L));
        listing.Models.Single(m => m.Id == "llama3.2:latest").ContextTokens.ShouldBe(8192, "never more than the model was trained with");
        listing.Models.Single(m => m.Id == "gemma3:4b").Vision.ShouldBeTrue();
        server.To("/api/show").Count.ShouldBe(4, "details of every local model; the cloud model is not asked");
    }

    [Fact]
    public async Task LmStudioIsFoundAndListsItsTextModelsWithTheRunningContext()
    {
        using var server = new FakeLmStudioServer();
        server.Instances["google/gemma-3-12b"] = ("google/gemma-3-12b", 32768);
        var (_, probe) = Create();

        var listing = await probe.ListModelsAsync(server.Root + "/v1/", string.Empty, None);

        listing.Ok.ShouldBeTrue();
        (listing.Kind, listing.Address).ShouldBe((EndpointKind.LmStudio, server.Root));
        listing.Models.Select(m => m.Id).ShouldBe(["qwen/qwen3-8b", "google/gemma-3-12b"], ignoreOrder: true, "no embedding model");
        listing.Models.Single(m => m.Id == "qwen/qwen3-8b").ShouldBe(new ListedModel("qwen/qwen3-8b", EndpointDefaults.ServerContextTokens, "Qwen3 8B", false, false, true,
            5_030_000_000));
        var gemma = listing.Models.Single(m => m.Id == "google/gemma-3-12b");
        (gemma.ContextTokens, gemma.Vision).ShouldBe((32768L, true), "a loaded model's running context");
    }

    [Fact]
    public async Task APlainServerStaysGeneric()
    {
        using var server = new FakeEndpointServer();
        var registry = new EndpointRegistry(new MemoryEndpointStore(), NullLogger<EndpointRegistry>.Instance);
        using var probe = new EndpointChatTransport(registry, server.Transport, NullLoggerFactory.Instance);

        var listing = await probe.ListModelsAsync(server.BaseUrl, string.Empty, None);

        (listing.Ok, listing.Kind, listing.Address).ShouldBe((true, EndpointKind.Generic, string.Empty));
        listing.Models.Count.ShouldBe(2);
    }

    [Fact]
    public async Task OllamaAndLmStudioAreFoundOnThisComputerAndADeadAddressIsNot()
    {
        using var ollama = new FakeOllamaServer();
        using var lmStudio = new FakeLmStudioServer();
        var dead = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        dead.Start();
        var deadPort = ((System.Net.IPEndPoint)dead.LocalEndpoint).Port;
        dead.Stop();
        var (_, probe) = Create(ollama.Root, lmStudio.Root, "http://127.0.0.1:" + deadPort, "not an address");

        var found = await probe.DetectAsync(None);

        found.ShouldBe([new DetectedServer(EndpointKind.Ollama, ollama.Root, "0.40.16", 3), new DetectedServer(EndpointKind.LmStudio, lmStudio.Root, string.Empty, 2)]);
    }

    [Fact]
    public async Task OllamaChatSendsTheContextTheSchemaThinkAndKeepAliveAndReadsTheAnswer()
    {
        using var server = new FakeOllamaServer { CardShare = 0.75 };
        server.Chat(200, FakeOllamaServer.Answer("""{"topics":["a","b"]}""", thinking: "I should list two."));
        var (chat, _) = await ChatAsync(Ollama(server, canThink: true));
        var image = new ImagePart([1, 2, 3], "image/png");

        var answer = await chat.SendAsync(new ChatRequest("You are helpful.", "Two topics.", Schema: Schema, MaxOutputTokens: 4000, Images: [image]), None);

        answer.Text.ShouldBe("""{"topics":["a","b"]}""", "the thinking is never part of the answer");
        (answer.Usage.Input, answer.Usage.Output, answer.StopReason).ShouldBe((120L, 45L, "stop"));
        var sent = JsonNode.Parse(server.To("/api/chat").Single().Body)!;
        sent["options"]!["num_ctx"]!.GetValue<long>().ShouldBe(16384, "the context goes with every request");
        sent["options"]!["num_predict"]!.GetValue<int>().ShouldBe(4000);
        sent["format"]!["properties"]!["topics"].ShouldNotBeNull("the schema goes as format");
        sent["think"]!.GetValue<bool>().ShouldBeFalse("a model that can think is told not to unless the user ticked it");
        sent["keep_alive"]!.GetValue<string>().ShouldBe("10m");
        sent["stream"]!.GetValue<bool>().ShouldBeFalse();
        sent["messages"]![0]!["role"]!.GetValue<string>().ShouldBe("system");
        sent["messages"]![1]!["images"]![0]!.GetValue<string>().ShouldBe(Convert.ToBase64String([1, 2, 3]));
        server.To("/api/ps").Count.ShouldBe(1, "after the answer the share on the graphics card is read for the log");
    }

    [Fact]
    public async Task OllamaChatAsksForJsonModeWithoutTheSchemaTickAndSendsNoThinkForAModelThatCannotThink()
    {
        using var server = new FakeOllamaServer();
        server.Chat(200, FakeOllamaServer.Answer("{}"));
        var (chat, _) = await ChatAsync(Ollama(server, schema: false, canThink: false));

        await chat.SendAsync(new ChatRequest(string.Empty, "x", Schema: Schema), None);

        var sent = JsonNode.Parse(server.To("/api/chat").Single().Body)!.AsObject();
        sent["format"]!.GetValue<string>().ShouldBe("json");
        sent.ContainsKey("think").ShouldBeFalse();
        sent["messages"]!.AsArray().Count.ShouldBe(1, "no empty system message");
    }

    [Fact]
    public async Task OllamaStreamsWithThinkingFirstAndReportsTheProgress()
    {
        using var server = new FakeOllamaServer();
        server.Chat(200, FakeOllamaServer.Stream("""{"topics":["x"]}"""), "application/x-ndjson");
        var (chat, _) = await ChatAsync(Ollama(server));
        var phases = new List<ChatPhase>();
        var progress = new SyncProgress<ChatProgress>(p => phases.Add(p.Phase));

        var answer = await chat.SendAsync(new ChatRequest("s", "u", Stream: true, Progress: progress), None);

        answer.Text.ShouldBe("""{"topics":["x"]}""");
        (answer.Usage.Input, answer.Usage.Output).ShouldBe((300L, 80L));
        phases.ShouldContain(ChatPhase.Thinking);
        phases.ShouldContain(ChatPhase.Writing);
        phases[^1].ShouldBe(ChatPhase.Done);
        JsonNode.Parse(server.To("/api/chat").Single().Body)!["stream"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task ACutOllamaAnswerIsTruncatedWithItsText()
    {
        using var server = new FakeOllamaServer();
        server.Chat(200, FakeOllamaServer.Answer("""{"topics":["a","b""", reason: "length"));
        var (chat, _) = await ChatAsync(Ollama(server));

        var ex = await Should.ThrowAsync<ProviderException>(() => chat.SendAsync(new ChatRequest("s", "u"), None));

        (ex.Code, ex.PartialText).ShouldBe(("truncated", """{"topics":["a","b"""));
    }

    [Fact]
    public async Task ARefusedFormatIsSentOnceMoreWithoutIt()
    {
        using var server = new FakeOllamaServer();
        server.Chat(400, """{"error":"invalid format: unsupported schema"}""").Chat(200, FakeOllamaServer.Answer("{}"));
        var (chat, _) = await ChatAsync(Ollama(server, canThink: true));

        await chat.SendAsync(new ChatRequest("s", "u", Schema: Schema), None);

        var bodies = server.To("/api/chat").Select(r => JsonNode.Parse(r.Body)!.AsObject()).ToList();
        bodies.Count.ShouldBe(2);
        bodies[0].ContainsKey("format").ShouldBeTrue();
        (bodies[1].ContainsKey("format"), bodies[1].ContainsKey("think")).ShouldBe((false, false));
        bodies[1]["options"]!["num_ctx"].ShouldNotBeNull("the context always goes");
    }

    [Fact]
    public async Task APromptLargerThanTheContextIsNotSent()
    {
        using var server = new FakeOllamaServer();
        var (chat, _) = await ChatAsync(Ollama(server, context: 1024));

        var ex = await Should.ThrowAsync<ProviderException>(() => chat.SendAsync(new ChatRequest("s", new string('x', 6000)), None));

        ex.Code.ShouldBe("too_large");
        server.To("/api/chat").ShouldBeEmpty();
    }

    [Fact]
    public async Task OllamaUnloadsAndWaitsUntilTheModelLeavesItsList()
    {
        using var server = new FakeOllamaServer { UnloadLag = 2 };
        server.Load("qwen3:8b");
        var (_, transport) = Create();
        var control = new OllamaModelControl(transport);

        (await control.UnloadAsync(Ollama(server), "qwen3:8b", None)).ShouldBe(EndpointUnload.Unloaded);

        var unload = JsonNode.Parse(server.To("/api/generate").Single().Body)!;
        (unload["model"]!.GetValue<string>(), unload["keep_alive"]!.GetValue<int>()).ShouldBe(("qwen3:8b", 0));
        server.To("/api/ps").Count.ShouldBe(4, "read first, then until the model is gone");
        server.Loaded.ShouldBeEmpty();
        (await control.UnloadAsync(LmStudio(new FakeLmStudioServer()), "qwen/qwen3-8b", None)).ShouldBe(EndpointUnload.NotMine, "another kind of server is not Ollama's");
    }

    [Fact]
    public async Task AnOllamaModelThatNeverLeavesIsAFailedUnload()
    {
        using var server = new FakeOllamaServer { UnloadLag = -1 };
        server.Load("qwen3:8b");
        var (_, transport) = Create();

        (await new OllamaModelControl(transport).UnloadAsync(Ollama(server), "qwen3:8b", None)).ShouldBe(EndpointUnload.Failed);
        server.To("/api/ps").Count.ShouldBe(11, "read first, then through the unload wait (1 s / 100 ms)");
    }

    [Fact]
    public async Task AnOllamaModelThatIsNotInMemoryIsNotAskedToUnload()
    {
        using var server = new FakeOllamaServer();
        var (_, transport) = Create();

        (await new OllamaModelControl(transport).UnloadAsync(Ollama(server), "qwen3:8b", None)).ShouldBe(EndpointUnload.Unloaded);

        server.To("/api/generate").ShouldBeEmpty("an unload request never makes Ollama load a model");
    }

    [Fact]
    public async Task OllamaReportsTheMemoryOnTheCardAndInMainMemory()
    {
        using var server = new FakeOllamaServer { CardShare = 0.5 };
        server.Load("qwen3:8b", 16384);
        var (_, transport) = Create();

        var loaded = (await transport.LoadedAsync(Ollama(server), None)).Single();

        loaded.Id.ShouldBe("qwen3:8b");
        loaded.ContextTokens.ShouldBe(16384);
        loaded.HasMemory.ShouldBeTrue();
        loaded.MainMemoryBytes.ShouldBe(loaded.TotalBytes - loaded.VideoMemoryBytes);
        Math.Abs(loaded.VideoMemoryBytes - (loaded.TotalBytes / 2)).ShouldBeLessThanOrEqualTo(1L);
    }

    [Fact]
    public async Task LmStudioLoadsTheModelWithItsContextBeforeTheFirstChatOnly()
    {
        using var server = new FakeLmStudioServer();
        server.Chat(200, FakeEndpointServer.Plain("{}")).Chat(200, FakeEndpointServer.Plain("{}"));
        var (chat, _) = await ChatAsync(LmStudio(server));

        await chat.SendAsync(new ChatRequest("s", "u", Schema: Schema), None);
        await chat.SendAsync(new ChatRequest("s", "u", Schema: Schema), None);

        var load = JsonNode.Parse(server.To("/api/v1/models/load").Single().Body)!;
        (load["model"]!.GetValue<string>(), load["context_length"]!.GetValue<long>()).ShouldBe(("qwen/qwen3-8b", 16384L));
        server.To("/v1/chat/completions").Count.ShouldBe(2, "the chat goes to LM Studio's OpenAI-compatible API under /v1");
        JsonNode.Parse(server.To("/v1/chat/completions")[0].Body)!["response_format"]!["type"]!.GetValue<string>().ShouldBe("json_schema");
        server.Instances.Values.ShouldBe([("qwen/qwen3-8b", 16384L)]);
    }

    [Fact]
    public async Task LmStudioReloadsAModelLoadedWithASmallerContext()
    {
        using var server = new FakeLmStudioServer();
        server.Instances["qwen/qwen3-8b"] = ("qwen/qwen3-8b", 4096);
        server.Chat(200, FakeEndpointServer.Plain("{}"));
        var (chat, _) = await ChatAsync(LmStudio(server));

        await chat.SendAsync(new ChatRequest("s", "u"), None);

        JsonNode.Parse(server.To("/api/v1/models/unload").Single().Body)!["instance_id"]!.GetValue<string>().ShouldBe("qwen/qwen3-8b");
        server.Instances.Values.ShouldBe([("qwen/qwen3-8b", 16384L)]);
    }

    [Fact]
    public async Task LmStudioUnloadsEveryInstanceAndConfirmsTheListIsEmpty()
    {
        using var server = new FakeLmStudioServer();
        server.Instances["qwen/qwen3-8b"] = ("qwen/qwen3-8b", 16384);
        server.Instances["qwen/qwen3-8b:2"] = ("qwen/qwen3-8b", 4096);
        server.Instances["google/gemma-3-12b"] = ("google/gemma-3-12b", 8192);
        var (_, transport) = Create();
        var control = new LmStudioModelControl(transport);

        (await control.UnloadAsync(LmStudio(server), "qwen/qwen3-8b", None)).ShouldBe(EndpointUnload.Unloaded);

        server.To("/api/v1/models/unload").Count.ShouldBe(2);
        server.Instances.Keys.ToList().ShouldBe(["google/gemma-3-12b"], "another model is left alone");
        using var ollama = new FakeOllamaServer();
        (await control.UnloadAsync(Ollama(ollama), "qwen3:8b", None)).ShouldBe(EndpointUnload.NotMine, "another kind of server is not LM Studio's");
    }

    [Fact]
    public async Task TheCoordinatorUnloadsTheOllamaModelBeforeAnImageModelTakesTheCard()
    {
        using var server = new FakeOllamaServer();
        server.Chat(200, FakeOllamaServer.Answer("{}"));
        var (registry, transport) = Create();
        var endpoint = Ollama(server);
        await registry.SaveAsync(endpoint, None);
        var image = new FakeLocalModel("z-image-turbo");
        var directory = new ModelDirectory([
            new EndpointModelSource(registry, [new OllamaModelControl(transport), new LmStudioModelControl(transport)], NullLogger<EndpointModelSource>.Instance),
            new FakeModelSource(image)]);
        var gpu = new GpuCoordinator(directory, new FakeVramProbe(new VramReading(16L << 30, 14L << 30, true, "test")), TestWaiters.Fast(), TimeProvider.System,
            NullLogger<GpuCoordinator>.Instance);
        var chats = new LeasedChatClientFactory(new SingleChatFactory(transport), gpu);

        await chats.Create(EndpointCodes.Catalog(endpoint)[0], string.Empty).SendAsync(new ChatRequest("s", "u"), None);
        server.Loaded.ShouldBe(["qwen3:8b"]);
        var lease = await gpu.AcquireAsync(image, null, None);
        await lease.DisposeAsync();

        server.Loaded.ShouldBeEmpty("the language model left the card before the image model took it");
        JsonNode.Parse(server.To("/api/generate").Single().Body)!["keep_alive"]!.GetValue<int>().ShouldBe(0);
        gpu.Loaded.ShouldBe(image);
    }

    private sealed class FakeModelSource(ILocalModel model) : ILocalModelSource
    {
        public ILocalModel? Create(CatalogModel catalog) => catalog.Provider == model.Provider && catalog.Id == model.Id ? model : null;
    }

    private sealed class SingleChatFactory(EndpointChatTransport transport) : IChatClientFactory
    {
        public IChatClient Create(CatalogModel model, string apiKey) => transport.CreateChat(model, apiKey);
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
