using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TMConcept.Providers.GoogleAuth;

/// <summary>
/// The one-time local address of a sign-in: listens on 127.0.0.1 on a free port the system picks (never on another interface), takes the
/// first answer that carries <c>code</c> or <c>error</c> for the root path, shows the browser a short page and closes. A plain TCP listener
/// (HttpListener needs a URL reservation for 127.0.0.1 on Windows). An answer with another state ends the sign-in with <c>state</c> (it was
/// not this sign-in's); a request for another path (the browser's icon) gets 404 and the listener waits on. Each connection is read
/// apart, so a browser connection that sends nothing does not hold up the real answer.
/// </summary>
public sealed class LoopbackCodeListener : IDisposable
{
    private const int MaxRequestBytes = 16 * 1024;
    private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener;
    private readonly LoopbackPages _pages;
    private int _stopped;

    /// <summary>Starts listening.</summary>
    public LoopbackCodeListener(LoopbackPages? pages = null)
    {
        _pages = pages ?? LoopbackPages.English;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>The port.</summary>
    public int Port { get; }

    /// <summary>The redirect address registered with the sign-in (and sent again with the code exchange).</summary>
    public Uri RedirectUri => new("http://127.0.0.1:" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/");

    /// <summary>Whether the listener is closed.</summary>
    public bool Stopped => Volatile.Read(ref _stopped) == 1;

    /// <summary>
    /// Waits for the code of the sign-in with <paramref name="state"/>; the listener is closed when this ends (a code, a refusal, a wrong
    /// state or the cancellation).
    /// </summary>
    public async Task<string> WaitForCodeAsync(string state, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(state);
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var registration = cancellationToken.Register(() => done.TrySetCanceled(cancellationToken)).ConfigureAwait(false);
        var accepting = AcceptAsync(state, done, stop.Token);
        try
        {
            return await done.Task.ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            Stop();
            try
            {
                await accepting.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The accept loop ends with the closed listener.
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            _listener.Stop();
            _listener.Dispose();
        }
    }

    private async Task AcceptAsync(string state, TaskCompletionSource<string> done, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            _ = Task.Run(() => AnswerAsync(client, state, done, cancellationToken), CancellationToken.None);
        }
    }

    private async Task AnswerAsync(TcpClient client, string state, TaskCompletionSource<string> done, CancellationToken cancellationToken)
    {
        using (client)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(ReadLimit);
            try
            {
                var stream = client.GetStream();
                var target = await ReadTargetAsync(stream, limit.Token).ConfigureAwait(false);
                if (target is null || !target.StartsWith('/') || PathOf(target) != "/")
                {
                    await WriteAsync(stream, 404, string.Empty, limit.Token).ConfigureAwait(false);
                    return;
                }

                var query = Query(target);
                if (query.TryGetValue("error", out var error))
                {
                    await WriteAsync(stream, 200, _pages.Failed, limit.Token).ConfigureAwait(false);
                    done.TrySetException(new GoogleSignInException("The sign-in was refused in the browser (" + Clean(error) + ").") { Code = "denied" });
                    return;
                }

                if (!query.TryGetValue("code", out var code) && !query.ContainsKey("state"))
                {
                    // Not an answer of the sign-in (the address opened by hand): nothing to do with it.
                    await WriteAsync(stream, 404, string.Empty, limit.Token).ConfigureAwait(false);
                    return;
                }

                if (!Pkce.SameState(state, query.GetValueOrDefault("state")))
                {
                    await WriteAsync(stream, 400, _pages.Failed, limit.Token).ConfigureAwait(false);
                    done.TrySetException(new GoogleSignInException("The answer to the local address was not of this sign-in.") { Code = "state" });
                    return;
                }

                if (string.IsNullOrEmpty(code))
                {
                    await WriteAsync(stream, 400, _pages.Failed, limit.Token).ConfigureAwait(false);
                    done.TrySetException(new GoogleSignInException("No code came back from the sign-in.") { Code = "code" });
                    return;
                }

                await WriteAsync(stream, 200, _pages.Done, limit.Token).ConfigureAwait(false);
                done.TrySetResult(code);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // One connection that broke or said nothing; the listener waits for the next.
            }
        }
    }

    /// <summary>The request target of a GET request line; null when it is not one or the request is too long.</summary>
    private static async Task<string?> ReadTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        var text = Encoding.ASCII.GetString(buffer, 0, length);
        var end = text.IndexOf("\r\n", StringComparison.Ordinal);
        var parts = (end >= 0 ? text[..end] : text).Split(' ');
        return parts.Length == 3 && parts[0] == "GET" && parts[2].StartsWith("HTTP/", StringComparison.Ordinal) ? parts[1] : null;
    }

    private static string PathOf(string target)
    {
        var mark = target.IndexOf('?', StringComparison.Ordinal);
        return mark >= 0 ? target[..mark] : target;
    }

    /// <summary>The query of a request target (the first value of each name, decoded).</summary>
    public static Dictionary<string, string> Query(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var mark = target.IndexOf('?', StringComparison.Ordinal);
        if (mark < 0)
        {
            return values;
        }

        foreach (var pair in target[(mark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var name = Uri.UnescapeDataString((equals >= 0 ? pair[..equals] : pair).Replace('+', ' '));
            var value = equals >= 0 ? Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' ')) : string.Empty;
            values.TryAdd(name, value);
        }

        return values;
    }

    private static string Clean(string error) => new([.. error.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').Take(40)]);

    private static async Task WriteAsync(NetworkStream stream, int status, string page, CancellationToken cancellationToken)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", _ => "Not Found" };
        var body = Encoding.UTF8.GetBytes(page);
        var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + reason
            + "\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: " + body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
