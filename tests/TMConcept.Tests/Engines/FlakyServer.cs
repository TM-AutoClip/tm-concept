using System.Net;
using System.Net.Http.Headers;

namespace TMConcept.Tests.Engines;

/// <summary>
/// A download server with a bad network for the downloader tests (no real network): each request takes the next scripted behaviour, then
/// the file is served normally (with HTTP Range support). Records the range each request asked for.
/// </summary>
internal sealed class FlakyServer(byte[] body) : HttpMessageHandler
{
    /// <summary>What the next requests get, in order.</summary>
    public Queue<Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>> Script { get; } = new();

    /// <summary>The start of the range each request asked for (null = the whole file).</summary>
    public List<long?> Ranges { get; } = [];

    /// <summary>Whether a Range header is honoured (false = the whole file is sent with 200, as some servers do).</summary>
    public bool HonourRange { get; set; } = true;

    /// <summary>A failure before any answer (TLS, DNS, refused).</summary>
    public static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Throw(Exception error) => (_, _) => throw error;

    /// <summary>A plain HTTP status without a body.</summary>
    public static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Status(HttpStatusCode status) => (_, _) => new HttpResponseMessage(status);

    /// <summary>The answer, cut after <paramref name="bytes"/> bytes with a reset connection.</summary>
    public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> CutAfter(int bytes) => (request, _) => Serve(request, bytes, stall: false);

    /// <summary>The answer's headers, then no data at all.</summary>
    public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Stall() => (request, _) => Serve(request, 0, stall: true);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Ranges.Add(request.Headers.Range?.Ranges.FirstOrDefault()?.From);
        var next = Script.Count > 0 ? Script.Dequeue() : (r, _) => Serve(r, null, stall: false);
        return Task.FromResult(next(request, cancellationToken));
    }

    private HttpResponseMessage Serve(HttpRequestMessage request, int? cut, bool stall)
    {
        var from = HonourRange ? request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0 : 0;
        var rest = body.AsMemory((int)from).ToArray();
        var content = new StreamContent(new CutStream(rest, cut, stall));
        content.Headers.ContentLength = rest.Length;
        var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = content };
        if (from > 0)
        {
            content.Headers.ContentRange = new ContentRangeHeaderValue(from, body.Length - 1, body.Length);
        }

        return response;
    }

    /// <summary>A body that ends with a reset after <c>cut</c> bytes, or never sends anything (<c>stall</c>).</summary>
    private sealed class CutStream(byte[] data, int? cut, bool stall) : Stream
    {
        private int _at;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position
        {
            get => _at;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("async only");

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (stall)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (cut is { } limit && _at >= limit)
            {
                throw new IOException("Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.",
                    new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset));
            }

            var count = Math.Min(Math.Min(buffer.Length, 4096), data.Length - _at);
            if (cut is { } end)
            {
                count = Math.Min(count, end - _at);
            }

            data.AsMemory(_at, count).CopyTo(buffer);
            _at += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
