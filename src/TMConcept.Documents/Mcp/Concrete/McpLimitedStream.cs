using TMConcept.Core.Research;

namespace TMConcept.Documents.Mcp;

/// <summary>Reads an HTTP answer and refuses it once it passes <see cref="ResearchLimits.McpMessageBytesMax"/> bytes (a server that never ends a line cannot fill the memory).</summary>
public sealed class McpLimitedStream(Stream inner) : Stream
{
    private long _read;

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position { get => _read; set => throw new NotSupportedException(); }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Count(int read)
    {
        _read += read;
        return _read > ResearchLimits.McpMessageBytesMax
            ? throw new McpException("size", $"An MCP message was longer than {ResearchLimits.McpMessageBytesMax >> 20} MB.")
            : read;
    }
}
