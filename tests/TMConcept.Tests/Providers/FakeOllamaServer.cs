using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace TMConcept.Tests.Providers;

/// <summary>
/// A fake Ollama on 127.0.0.1 with the parts the app uses: <c>/api/version</c>, <c>/api/tags</c>, <c>/api/show</c>, <c>/api/ps</c> (the loaded
/// models with <c>size</c> and <c>size_vram</c>), <c>/api/chat</c> (queued answers; a chat loads its model) and <c>/api/generate</c> with
/// <c>keep_alive: 0</c> (unload; <see cref="UnloadLag"/> readings of <c>/api/ps</c> still show the model, as the real one frees memory a
/// moment later). Everything else is a 404, like the real one for OpenAI-style probes.
/// </summary>
internal sealed class FakeOllamaServer : IDisposable
{
    private readonly FakeLocalServer _server;
    private readonly ConcurrentQueue<FakeEndpointAnswer> _chats = new();
    private readonly ConcurrentDictionary<string, (long Size, long Vram, long Context)> _loaded = new(StringComparer.Ordinal);
    private int _lag = -1;

    public FakeOllamaServer() => _server = new FakeLocalServer(Respond);

    public string Root => _server.Root;

    public ConcurrentQueue<RecordedRequest> Requests => _server.Requests;

    public List<RecordedRequest> To(string path) => _server.To(path);

    /// <summary>The installed models: name → (capabilities, architecture, trained context, size on disk).</summary>
    public Dictionary<string, (string[] Capabilities, string Architecture, long Context, long Size)> Models { get; } = new(StringComparer.Ordinal)
    {
        ["qwen3:8b"] = (["completion", "tools", "thinking"], "qwen3", 40960, 5_225_376_047),
        ["llama3.2:latest"] = (["completion", "tools"], "llama", 8192, 2_019_393_189),
        ["gemma3:4b"] = (["completion", "vision"], "gemma3", 131072, 3_338_801_804),
        ["nomic-embed-text:latest"] = (["embedding"], "nomic-bert", 2048, 274_302_450),
        ["gpt-oss:120b-cloud"] = (["completion", "thinking"], "gptoss", 131072, 384),
    };

    /// <summary>How much of a model a chat puts on the card (the rest: main memory).</summary>
    public double CardShare { get; set; } = 1.0;

    /// <summary>How many readings of <c>/api/ps</c> still show a model after its unload (-1: it never leaves).</summary>
    public int UnloadLag { get; set; }

    public IReadOnlyCollection<string> Loaded => [.. _loaded.Keys];

    public FakeOllamaServer Chat(int status, string body, string mediaType = "application/json")
    {
        _chats.Enqueue(new FakeEndpointAnswer(status, body, mediaType));
        return this;
    }

    /// <summary>Puts a model in memory as if another program loaded it.</summary>
    public void Load(string name, long context = 4096)
    {
        var size = Models.TryGetValue(name, out var model) ? model.Size + (512L << 20) : 1L << 30;
        _loaded[name] = (size, (long)(size * CardShare), context);
    }

    /// <summary>A whole answer of <c>/api/chat</c>.</summary>
    public static string Answer(string content, string reason = "stop", string thinking = "") =>
        new JsonObject
        {
            ["model"] = "qwen3:8b",
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content, ["thinking"] = thinking },
            ["done"] = true,
            ["done_reason"] = reason,
            ["prompt_eval_count"] = 120,
            ["eval_count"] = 45,
        }.ToJsonString();

    /// <summary>A streamed answer of <c>/api/chat</c>: thinking lines, then the content in two lines, then the closing line with the counts.</summary>
    public static string Stream(string content, string reason = "stop")
    {
        var half = content.Length / 2;
        var lines = new List<string>
        {
            new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = string.Empty, ["thinking"] = "Let me {think}." }, ["done"] = false }.ToJsonString(),
        };
        foreach (var part in new[] { content[..half], content[half..] })
        {
            lines.Add(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = part }, ["done"] = false }.ToJsonString());
        }

        lines.Add(new JsonObject
        {
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = string.Empty },
            ["done"] = true,
            ["done_reason"] = reason,
            ["prompt_eval_count"] = 300,
            ["eval_count"] = 80,
        }.ToJsonString());
        return string.Join("\n", lines) + "\n";
    }

    public void Dispose() => _server.Dispose();

    private FakeEndpointAnswer Respond(RecordedRequest request)
    {
        var path = request.Uri.AbsolutePath;
        var body = request.Body.Length > 0 ? JsonNode.Parse(request.Body) : null;
        switch (request.Method.Method, path)
        {
            case ("GET", "/"):
                return new FakeEndpointAnswer(200, "Ollama is running", "text/plain");
            case ("GET", "/api/version"):
                return Json("""{"version":"0.40.16"}""");
            case ("GET", "/api/tags"):
                return Json(new JsonObject
                {
                    ["models"] = new JsonArray([.. Models.Select(m => (JsonNode)new JsonObject
                    {
                        ["name"] = m.Key,
                        ["model"] = m.Key,
                        ["size"] = m.Value.Size,
                        ["details"] = new JsonObject { ["family"] = m.Value.Architecture },
                        ["remote_host"] = m.Key.EndsWith("-cloud", StringComparison.Ordinal) ? "https://ollama.com:443" : null,
                    })]),
                }.ToJsonString());
            case ("POST", "/api/show"):
            {
                var name = body?["model"]?.GetValue<string>() ?? string.Empty;
                if (!Models.TryGetValue(name, out var model))
                {
                    return new FakeEndpointAnswer(404, """{"error":"model not found"}""", "application/json");
                }

                return Json(new JsonObject
                {
                    ["capabilities"] = new JsonArray([.. model.Capabilities.Select(c => (JsonNode)JsonValue.Create(c))]),
                    ["model_info"] = new JsonObject
                    {
                        ["general.architecture"] = model.Architecture,
                        [model.Architecture + ".context_length"] = model.Context,
                    },
                }.ToJsonString());
            }

            case ("GET", "/api/ps"):
                if (_lag > 0)
                {
                    _lag--;
                }
                else if (_lag == 0)
                {
                    _loaded.Clear();
                    _lag = -1;
                }

                return Json(new JsonObject
                {
                    ["models"] = new JsonArray([.. _loaded.Select(m => (JsonNode)new JsonObject
                    {
                        ["name"] = m.Key,
                        ["model"] = m.Key,
                        ["size"] = m.Value.Size,
                        ["size_vram"] = m.Value.Vram,
                        ["context_length"] = m.Value.Context,
                    })]),
                }.ToJsonString());
            case ("POST", "/api/generate"):
            {
                var name = body?["model"]?.GetValue<string>() ?? string.Empty;
                if (!Models.ContainsKey(name))
                {
                    return new FakeEndpointAnswer(404, """{"error":"model not found"}""", "application/json");
                }

                if (body?["keep_alive"]?.ToJsonString() == "0")
                {
                    // Unloaded after UnloadLag more readings; the list of the others stays (the fake keeps it simple: all go).
                    _lag = UnloadLag;
                    if (UnloadLag == 0)
                    {
                        _loaded.TryRemove(name, out _);
                        _lag = -1;
                    }

                    return Json("""{"done":true,"done_reason":"unload","response":""}""");
                }

                return Json("""{"done":true,"done_reason":"load","response":""}""");
            }

            case ("POST", "/api/chat"):
            {
                var name = body?["model"]?.GetValue<string>() ?? string.Empty;
                var context = body?["options"]?["num_ctx"]?.GetValue<long>() ?? 4096;
                if (Models.ContainsKey(name))
                {
                    Load(name, context);
                }

                return _chats.TryDequeue(out var chat) ? chat : new FakeEndpointAnswer(500, """{"error":"no answer queued"}""", "application/json");
            }

            default:
                return new FakeEndpointAnswer(404, "404 page not found", "text/plain");
        }
    }

    private static FakeEndpointAnswer Json(string body) => new(200, body, "application/json");
}
