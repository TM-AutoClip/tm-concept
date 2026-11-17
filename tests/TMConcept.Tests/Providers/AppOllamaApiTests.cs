using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Chat;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;
using TMConcept.Core.Tools;
using TMConcept.Providers.AppOllama;
using TMConcept.Providers.Catalog;
using TMConcept.Providers.Chat;
using TMConcept.Providers.Endpoints;

namespace TMConcept.Tests.Providers;

/// <summary>
/// The app's own Ollama API (0.1.20): a pull streamed line by line with progress, an error or an early end as an install failure, a cancel
/// that a second pull continues, an unload with <c>keep_alive: 0</c>, and the chat of an app model through Ollama's own API. Fake servers
/// only: the user's Ollama and the internet are never reached.
/// </summary>
public sealed class AppOllamaApiTests : IDisposable
{
    private static readonly Uri Root = new("http://127.0.0.1:11534/");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "app-ollama-api-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    [Fact]
    public void PullLinesAreRead()
    {
        var line = AppModelPullParser.Read("""{"status":"pulling 6a0746a1ec1a","digest":"sha256:6a07","total":2019377376,"completed":241970}""")!;
        (line.Status, line.Digest, line.Total, line.Completed, line.Success).ShouldBe(("pulling 6a0746a1ec1a", "sha256:6a07", 2019377376L, 241970L, false));
        AppModelPullParser.Read("""{"status":"success"}""")!.Success.ShouldBeTrue();
        AppModelPullParser.Read("""{"error":"pull model manifest: file does not exist"}""")!.Error.ShouldStartWith("pull model manifest");
        AppModelPullParser.Read("not json").ShouldBeNull();
        AppModelPullParser.Read("   ").ShouldBeNull();
        AppModelPullParser.Read("[1,2]").ShouldBeNull();
    }

    [Fact]
    public async Task APullReportsEveryLineUntilSuccess()
    {
        var http = new FakeHttp().Answer(HttpStatusCode.OK, Lines(
            """{"status":"pulling manifest"}""",
            """{"status":"pulling a","digest":"sha256:a","total":100,"completed":50}""",
            "garbage that is skipped",
            """{"status":"pulling a","digest":"sha256:a","total":100,"completed":100}""",
            """{"status":"verifying sha256 digest"}""",
            """{"status":"success"}"""), "application/x-ndjson");
        using var api = new AppOllamaApi(new EndpointTransport(http.Transport), NullLoggerFactory.Instance);
        var seen = new List<AppModelPullProgress>();

        await api.PullAsync(Root, "qwen3.5:4b-q4_K_M", new Collect(seen), CancellationToken.None);

        seen.Select(s => s.Status).ShouldBe(["pulling manifest", "pulling a", "pulling a", "verifying sha256 digest", "success"]);
        var request = http.Requests.ShouldHaveSingleItem();
        request.Uri.ShouldBe(new Uri(Root, "api/pull"));
        request.Uri.Port.ShouldNotBe(AppOllamaDefaults.UserOllamaPort);
        var body = JsonNode.Parse(request.Body)!;
        body["model"]!.GetValue<string>().ShouldBe("qwen3.5:4b-q4_K_M");
        body["stream"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task AnErrorLineOrAnEarlyEndIsAnInstallFailure()
    {
        var http = new FakeHttp()
            .Answer(HttpStatusCode.OK, Lines("""{"status":"pulling manifest"}""", """{"error":"max retries exceeded: unexpected EOF"}"""))
            .Answer(HttpStatusCode.OK, Lines("""{"status":"pulling a","digest":"sha256:a","total":100,"completed":10}"""))
            .Answer(HttpStatusCode.NotFound, """{"error":"pull model manifest: file does not exist"}""");
        using var api = new AppOllamaApi(new EndpointTransport(http.Transport), NullLoggerFactory.Instance);

        var error = await Should.ThrowAsync<ToolInstallException>(() => api.PullAsync(Root, "gemma4:e4b-it-qat", null, CancellationToken.None));
        (error.Code, error.Detail).ShouldBe(("pull", "max retries exceeded: unexpected EOF"));
        (await Should.ThrowAsync<ToolInstallException>(() => api.PullAsync(Root, "gemma4:e4b-it-qat", null, CancellationToken.None))).Code.ShouldBe("connection");
        var refused = await Should.ThrowAsync<ToolInstallException>(() => api.PullAsync(Root, "gemma4:e4b-it-qat", null, CancellationToken.None));
        refused.Detail.ShouldBe("pull model manifest: file does not exist");
        await Should.ThrowAsync<ArgumentException>(() => api.PullAsync(Root, "../evil", null, CancellationToken.None));
    }

    [Fact]
    public async Task ACancelledPullStopsAndTheNextPullGoesOn()
    {
        var http = new FakeHttp()
            .Answer(() => new StreamContent(new StallingStream(Lines("""{"status":"pulling a","digest":"sha256:a","total":100,"completed":40}"""))))
            .Answer(HttpStatusCode.OK, Lines("""{"status":"pulling a","digest":"sha256:a","total":100,"completed":100}""", """{"status":"success"}"""));
        using var api = new AppOllamaApi(new EndpointTransport(http.Transport), NullLoggerFactory.Instance);
        using var cancel = new CancellationTokenSource();
        var seen = new List<AppModelPullProgress>();

        var first = api.PullAsync(Root, "qwen3.5:9b-q4_K_M", new Collect(seen, p =>
        {
            if (p.Completed == 40)
            {
                cancel.Cancel();
            }
        }), cancel.Token);
        await Should.ThrowAsync<OperationCanceledException>(() => first);

        // Ollama kept the 40 bytes it had; the next pull asks again and its first line already says more.
        await api.PullAsync(Root, "qwen3.5:9b-q4_K_M", new Collect(seen), CancellationToken.None);
        http.Requests.Count.ShouldBe(2);
        http.Requests[1].Body.ShouldBe(http.Requests[0].Body);
        seen.Select(s => s.Completed).ShouldBe([40L, 100L, 0L]);
    }

    [Fact]
    public async Task AnUnloadAsksWithKeepAliveZeroAndWaitsUntilTheModelIsGone()
    {
        using var server = new FakeOllamaServer { UnloadLag = 1 };
        server.Load("qwen3:8b");
        using var api = new AppOllamaApi(FakeLocalServer.Transport(), NullLoggerFactory.Instance);
        var root = new Uri(server.Root + "/");

        (await api.IsUpAsync(root, CancellationToken.None)).ShouldBeTrue();
        (await api.LoadedAsync(root, CancellationToken.None)).ShouldBe(["qwen3:8b"]);
        (await api.UnloadAsync(root, "qwen3:8b", CancellationToken.None)).ShouldBeTrue();

        var unload = JsonNode.Parse(server.To("/api/generate").ShouldHaveSingleItem().Body)!;
        unload["model"]!.GetValue<string>().ShouldBe("qwen3:8b");
        unload["keep_alive"]!.GetValue<int>().ShouldBe(0);
        (await api.LoadedAsync(root, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAppModelChatsThroughTheAppsOwnServerOnly()
    {
        using var server = new FakeOllamaServer();
        server.Chat(200, FakeOllamaServer.Answer("""{"topics":["a"]}"""));
        var store = new AppModelStore(new AppOllamaLayout(Path.Combine(_folder, "models"), Path.Combine(_folder, "home")));
        Core.AppModelFiles.Write(store, "qwen3:8b", "weights");
        var registry = new AppModelRegistry(store, NullLogger<AppModelRegistry>.Instance);
        var catalog = new MergedModelCatalog(new EmbeddedModelCatalog(), new EndpointRegistry(new MemoryEndpointStore(), NullLogger<EndpointRegistry>.Instance), registry);
        var running = new FakeRunningAppServer(server.Root);
        using var api = new AppOllamaApi(FakeLocalServer.Transport(), NullLoggerFactory.Instance);
        var factory = new ChatClientFactory([new AppOllamaChatTransport(api, running, registry, NullLoggerFactory.Instance)]);

        var model = catalog.Find(ModelProviders.AppOllama, "qwen3:8b").ShouldNotBeNull();
        catalog.With(ModelCapability.Chat).ShouldContain(model);
        var chat = factory.Create(model, string.Empty);
        chat.ShouldBeOfType<AppOllamaChatClient>();

        var answer = await chat.SendAsync(new ChatRequest("system", "Two topics.", Schema: JsonNode.Parse("""{"type":"object"}""")!.AsObject()), CancellationToken.None);

        answer.Text.ShouldBe("""{"topics":["a"]}""");
        running.Starts.ShouldBe(1);
        var sent = JsonNode.Parse(server.To("/api/chat").ShouldHaveSingleItem().Body)!.AsObject();
        sent["model"]!.GetValue<string>().ShouldBe("qwen3:8b");
        sent["options"]!["num_ctx"]!.GetValue<long>().ShouldBe(model.ContextTokens);
        sent["format"]!["type"]!.GetValue<string>().ShouldBe("object", "the schema goes as Ollama's grammar");
        AppOllamaChatClient.Endpoint(running.Address!).Kind.ShouldBe(EndpointKind.Ollama);

        // Removing the model from the folder takes it out of every picker after the next look at the disk.
        File.Delete(store.ManifestPath("qwen3:8b"));
        registry.Refresh();
        catalog.Find(ModelProviders.AppOllama, "qwen3:8b").ShouldBeNull();
    }

    private sealed class Collect(List<AppModelPullProgress> into, Action<AppModelPullProgress>? also = null) : IProgress<AppModelPullProgress>
    {
        public void Report(AppModelPullProgress value)
        {
            into.Add(value);
            also?.Invoke(value);
        }
    }
}
