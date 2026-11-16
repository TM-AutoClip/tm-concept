using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMConcept.Providers.Common;
using TMConcept.Providers.Endpoints;

namespace TMConcept.Tests.Providers;

/// <summary>
/// A fake OpenAI-compatible local server on 127.0.0.1 (a random free port; <see cref="HttpListener"/>), so the local server code runs over
/// real sockets and nothing else is reached: <c>GET {base}/models</c> and <c>POST {base}/chat/completions</c> answer from their queues
/// (an empty queue: the model list <see cref="Models"/>, a chat 500). Every request is recorded.
/// </summary>
internal sealed class FakeEndpointServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentQueue<FakeEndpointAnswer> _models = new();
    private readonly ConcurrentQueue<FakeEndpointAnswer> _chats = new();
    private readonly Task _loop;
    private readonly string _basePath = string.Empty;

    public FakeEndpointServer(string basePath = "v1")
    {
        for (var attempt = 0; ; attempt++)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                _listener.Start();
                BaseUrl = $"http://127.0.0.1:{port}/{basePath}";
                _basePath = "/" + basePath;
                break;
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                // The port was taken between the probe and the start: another one.
            }
        }

        _loop = Task.Run(LoopAsync);
    }

    /// <summary>llama-server's <c>GET /props</c> answer (null: no such page, a 404 like Lemonade, Ollama and LM Studio).</summary>
    public string? Props { get; set; }

    /// <summary>The address to type (no trailing slash).</summary>
    public string BaseUrl { get; } = string.Empty;

    /// <summary>The default answer of the model list.</summary>
    public string Models { get; set; } = """{"object":"list","data":[{"id":"bonsai-27b","context_length":32768},{"id":"qwen3-8b"}]}""";

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public List<RecordedRequest> Chats => Requests.Where(r => r.Uri.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal)).ToList();

    /// <summary>The local server transport pointed at the real network with instant waits (the 503 retries record their wait).</summary>
    public EndpointTransport Transport => new(ProviderTransport.Network with
    {
        Delay = (wait, _) =>
        {
            Delays.Enqueue(wait);
            return Task.CompletedTask;
        },
    });

    public ConcurrentQueue<TimeSpan> Delays { get; } = new();

    public FakeEndpointServer ModelList(int status, string body, string mediaType = "application/json")
    {
        _models.Enqueue(new FakeEndpointAnswer(status, body, mediaType));
        return this;
    }

    public FakeEndpointServer Chat(int status, string body, string mediaType = "application/json")
    {
        _chats.Enqueue(new FakeEndpointAnswer(status, body, mediaType));
        return this;
    }

    /// <summary>A plain answer with <paramref name="content"/> and no usage.</summary>
    public static string Plain(string content) =>
        """{"id":"x","object":"chat.completion","choices":[{"index":0,"message":{"role":"assistant","content":""" +
        System.Text.Json.JsonSerializer.Serialize(content) + """},"finish_reason":"stop"}]}""";

    /// <summary>A streamed answer: thinking chunks (<c>reasoning_content</c>), then <paramref name="content"/> in two pieces, no usage.</summary>
    public static string Stream(string content)
    {
        var half = content.Length / 2;
        var builder = new StringBuilder();
        builder.Append("data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"reasoning_content\":\"Let me think {about it}.\"}}]}\n\n");
        builder.Append("data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning\":\"Still thinking.\"}}]}\n\n");
        foreach (var part in new[] { content[..half], content[half..] })
        {
            builder.Append("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":").Append(System.Text.Json.JsonSerializer.Serialize(part)).Append("}}]}\n\n");
        }

        builder.Append("data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n");
        builder.Append("data: [DONE]\n\n");
        return builder.ToString();
    }

    public void Dispose()
    {
        _listener.Close();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The loop ends with the closed listener.
        }
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            await AnswerAsync(context).ConfigureAwait(false);
        }
    }

    private async Task AnswerAsync(HttpListenerContext context)
    {
        var request = context.Request;
        string body;
        using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        var headers = request.Headers.AllKeys.OfType<string>().ToDictionary(k => k, k => request.Headers[k] ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        Requests.Enqueue(new RecordedRequest(new HttpMethod(request.HttpMethod), request.Url!, headers, body));
        var path = request.Url!.AbsolutePath;
        // Only the list under the base path: LM Studio's /api/v1/models probe of a /v1 server is a 404 here, like on a plain server.
        var answer = path == _basePath + "/models"
            ? _models.TryDequeue(out var listed) ? listed : new FakeEndpointAnswer(200, Models, "application/json")
            : path.EndsWith("/chat/completions", StringComparison.Ordinal)
                ? _chats.TryDequeue(out var chat) ? chat : new FakeEndpointAnswer(500, """{"error":{"message":"no answer queued"}}""", "application/json")
                : path == "/props" && Props is { } props
                    ? new FakeEndpointAnswer(200, props, "application/json")
                    : new FakeEndpointAnswer(404, "not found", "text/plain");
        var bytes = Encoding.UTF8.GetBytes(answer.Body);
        try
        {
            context.Response.StatusCode = answer.Status;
            context.Response.ContentType = answer.MediaType + "; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            // The client went away.
        }
    }
}
