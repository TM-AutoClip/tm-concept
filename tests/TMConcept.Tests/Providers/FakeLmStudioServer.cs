using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace TMConcept.Tests.Providers;

/// <summary>
/// A fake LM Studio 0.4 on 127.0.0.1: <c>GET /api/v1/models</c> (downloaded models with their loaded instances), <c>POST /api/v1/models/load</c>
/// (adds an instance with the asked context), <c>POST /api/v1/models/unload</c>, and the OpenAI-compatible <c>/v1/models</c> and
/// <c>/v1/chat/completions</c> (queued answers; a chat with a model that is not loaded loads it with LM Studio's default 4 096 context,
/// as its just-in-time loading does). <c>/api/version</c> is a 404.
/// </summary>
internal sealed class FakeLmStudioServer : IDisposable
{
    private readonly FakeLocalServer _server;
    private readonly ConcurrentQueue<FakeEndpointAnswer> _chats = new();
    private readonly object _gate = new();

    public FakeLmStudioServer() => _server = new FakeLocalServer(Respond);

    public string Root => _server.Root;

    public ConcurrentQueue<RecordedRequest> Requests => _server.Requests;

    public List<RecordedRequest> To(string path) => _server.To(path);

    /// <summary>The downloaded models: key → (type, display name, max context, vision, size).</summary>
    public Dictionary<string, (string Type, string Display, long MaxContext, bool Vision, long Size)> Models { get; } = new(StringComparer.Ordinal)
    {
        ["qwen/qwen3-8b"] = ("llm", "Qwen3 8B", 40960, false, 5_030_000_000),
        ["google/gemma-3-12b"] = ("llm", "Gemma 3 12B", 131072, true, 8_100_000_000),
        ["text-embedding-nomic-embed-text-v1.5"] = ("embedding", "Nomic Embed Text v1.5", 2048, false, 84_000_000),
    };

    /// <summary>Loaded instances: instance id → (model key, context).</summary>
    public Dictionary<string, (string Key, long Context)> Instances { get; } = new(StringComparer.Ordinal);

    public FakeLmStudioServer Chat(int status, string body, string mediaType = "application/json")
    {
        _chats.Enqueue(new FakeEndpointAnswer(status, body, mediaType));
        return this;
    }

    public void Dispose() => _server.Dispose();

    private FakeEndpointAnswer Respond(RecordedRequest request)
    {
        var body = request.Body.Length > 0 ? JsonNode.Parse(request.Body) : null;
        lock (_gate)
        {
            switch (request.Method.Method, request.Uri.AbsolutePath)
            {
                case ("GET", "/api/v1/models"):
                    return Json(new JsonObject
                    {
                        ["models"] = new JsonArray([.. Models.Select(m => (JsonNode)new JsonObject
                        {
                            ["type"] = m.Value.Type,
                            ["publisher"] = "lmstudio-community",
                            ["key"] = m.Key,
                            ["display_name"] = m.Value.Display,
                            ["size_bytes"] = m.Value.Size,
                            ["max_context_length"] = m.Value.MaxContext,
                            ["format"] = "gguf",
                            ["capabilities"] = new JsonObject { ["vision"] = m.Value.Vision, ["trained_for_tool_use"] = true },
                            ["loaded_instances"] = new JsonArray([.. Instances.Where(i => i.Value.Key == m.Key).Select(i => (JsonNode)new JsonObject
                            {
                                ["id"] = i.Key,
                                ["config"] = new JsonObject { ["context_length"] = i.Value.Context, ["flash_attention"] = true },
                            })]),
                        })]),
                    }.ToJsonString());
                case ("POST", "/api/v1/models/load"):
                {
                    var key = body?["model"]?.GetValue<string>() ?? string.Empty;
                    if (!Models.ContainsKey(key))
                    {
                        return new FakeEndpointAnswer(404, """{"error":{"message":"model not found"}}""", "application/json");
                    }

                    var context = body?["context_length"]?.GetValue<long>() ?? 4096;
                    var id = Load(key, context);
                    return Json(new JsonObject
                    {
                        ["type"] = "llm",
                        ["instance_id"] = id,
                        ["load_time_seconds"] = 2.5,
                        ["status"] = "loaded",
                        ["load_config"] = new JsonObject { ["context_length"] = context },
                    }.ToJsonString());
                }

                case ("POST", "/api/v1/models/unload"):
                {
                    var id = body?["instance_id"]?.GetValue<string>() ?? string.Empty;
                    return Instances.Remove(id)
                        ? Json(new JsonObject { ["instance_id"] = id }.ToJsonString())
                        : new FakeEndpointAnswer(404, """{"error":{"message":"no such instance"}}""", "application/json");
                }

                case ("GET", "/v1/models"):
                    return Json(new JsonObject
                    {
                        ["object"] = "list",
                        ["data"] = new JsonArray([.. Models.Keys.Select(k => (JsonNode)new JsonObject { ["id"] = k, ["object"] = "model" })]),
                    }.ToJsonString());
                case ("POST", "/v1/chat/completions"):
                {
                    var key = body?["model"]?.GetValue<string>() ?? string.Empty;
                    if (Models.ContainsKey(key) && Instances.All(i => i.Value.Key != key))
                    {
                        Load(key, 4096);
                    }

                    return _chats.TryDequeue(out var chat) ? chat : new FakeEndpointAnswer(500, """{"error":{"message":"no answer queued"}}""", "application/json");
                }

                default:
                    return new FakeEndpointAnswer(404, """{"error":"Unexpected endpoint or method."}""", "application/json");
            }
        }
    }

    private string Load(string key, long context)
    {
        var id = key;
        for (var n = 2; Instances.ContainsKey(id); n++)
        {
            id = key + ":" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        Instances[id] = (key, context);
        return id;
    }

    private static FakeEndpointAnswer Json(string body) => new(200, body, "application/json");
}
