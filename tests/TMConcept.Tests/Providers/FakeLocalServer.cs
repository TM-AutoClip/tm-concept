using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMConcept.Providers.Common;
using TMConcept.Providers.Endpoints;

namespace TMConcept.Tests.Providers;

/// <summary>
/// A fake HTTP server on 127.0.0.1 (a random free port) that answers every request with <paramref name="respond"/> and records it: the base
/// of the fake Ollama and LM Studio servers. Nothing else is reached.
/// </summary>
internal sealed class FakeLocalServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<RecordedRequest, FakeEndpointAnswer> _respond;
    private readonly Task _loop;

    public FakeLocalServer(Func<RecordedRequest, FakeEndpointAnswer> respond)
    {
        _respond = respond;
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
                Root = $"http://127.0.0.1:{port}";
                break;
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                // The port was taken between the probe and the start: another one.
            }
        }

        _loop = Task.Run(LoopAsync);
    }

    /// <summary>The server's address without a trailing slash.</summary>
    public string Root { get; } = string.Empty;

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public List<RecordedRequest> To(string path) => Requests.Where(r => r.Uri.AbsolutePath == path).ToList();

    /// <summary>The local server transport on the real network with instant waits and nothing looked for by itself.</summary>
    public static EndpointTransport Transport(params string[] detect) => new(ProviderTransport.Network with { Delay = (_, _) => Task.CompletedTask })
    {
        DetectAddresses = detect,
        DetectTimeout = TimeSpan.FromSeconds(5),
        UnloadTimeout = TimeSpan.FromSeconds(1),
        UnloadPoll = TimeSpan.FromMilliseconds(100),
    };

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
        var recorded = new RecordedRequest(new HttpMethod(request.HttpMethod), request.Url!, headers, body);
        Requests.Enqueue(recorded);
        FakeEndpointAnswer answer;
        try
        {
            answer = _respond(recorded);
        }
        catch (Exception ex)
        {
            answer = new FakeEndpointAnswer(500, "{\"error\":\"" + ex.GetType().Name + "\"}", "application/json");
        }

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
