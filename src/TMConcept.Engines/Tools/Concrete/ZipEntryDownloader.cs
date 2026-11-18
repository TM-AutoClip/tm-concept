using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>
/// Downloads one pinned entry of a published zip (<see cref="ZipEntryArchive"/>, 0.1.27): an HTTP Range request for exactly the entry's
/// bytes, over HTTPS from an allowed host (every redirect hop is checked like in <see cref="ToolDownloader"/>), kept in
/// <c>&lt;file&gt;.zpart</c>, inflated into <c>&lt;file&gt;.part</c> while its SHA-256 is computed, and moved into place only when the size and
/// the SHA-256 of the unpacked file match the pin (otherwise both are deleted: code <c>hash</c>). A server that does not answer the range
/// with exactly those bytes is refused (code <c>download</c>): the whole package is never taken instead. Network failures get a few tries
/// (<see cref="DownloadPolicy"/>); a cancel by the user stays an <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class ZipEntryDownloader : IZipEntryDownloader, IDisposable
{
    private const int MaxRedirects = 5;

    private readonly ILogger<ZipEntryDownloader> _logger;
    private readonly DownloadPolicy _policy;
    private readonly HttpClient _http;

    /// <summary>Creates; <paramref name="policy"/> null = <see cref="DownloadPolicy.Default"/>.</summary>
    public ZipEntryDownloader(DownloadTransport transport, ILogger<ZipEntryDownloader> logger, DownloadPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _logger = logger;
        _policy = policy ?? DownloadPolicy.Default;
        _http = new HttpClient(transport.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <inheritdoc />
    public async Task DownloadAsync(ZipEntryArchive entry, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrEmpty(file);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await AttemptAsync(entry, file, progress, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("{File}: download cancelled", entry.FileName);
                throw;
            }
            catch (IOException ex) when (ToolDownloader.DiskFull(ex))
            {
                _logger.LogError(ex, "{File}: the disk is full", entry.FileName);
                throw new ToolInstallException("The disk is full.", ex) { Code = "disk", Detail = ex.Message.Trim() };
            }
            catch (Exception ex) when (Transient(ex))
            {
                var failure = ToolDownloader.Classify(ex, attempt);
                if (attempt >= _policy.Attempts)
                {
                    _logger.LogError(ex, "{File}: the download failed after {Attempts} tries ({Code}: {Detail})", entry.FileName, attempt, failure.Code, failure.Detail);
                    throw failure;
                }

                var delay = _policy.DelayAfter(attempt);
                _logger.LogWarning(ex, "{File}: try {Attempt} of {Attempts} failed ({Code}: {Detail}); trying again in {Seconds:0.#} s", entry.FileName, attempt,
                    _policy.Attempts, failure.Code, failure.Detail, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private async Task AttemptAsync(ZipEntryArchive entry, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var packed = file + ".zpart";
        var part = file + ".part";
        try
        {
            await ReceiveAsync(entry, packed, progress, cancellationToken).ConfigureAwait(false);
            var (bytes, actual) = await UnpackAsync(entry, packed, part, cancellationToken).ConfigureAwait(false);
            if (bytes != entry.Bytes || !string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                _logger.LogError("{File} {Version}: SHA-256 mismatch (expected {Expected} of {ExpectedBytes} bytes, got {Actual} of {Bytes}); the file was deleted",
                    entry.FileName, entry.Version, entry.Sha256, entry.Bytes, actual, bytes);
                throw new ToolInstallException("The downloaded file does not match its pinned SHA-256; it was deleted.") { Code = "hash" };
            }

            File.Move(part, file, overwrite: true);
            _logger.LogInformation("{File} {Version} downloaded and verified", entry.FileName, entry.Version);
        }
        finally
        {
            File.Delete(packed);
            if (File.Exists(part))
            {
                File.Delete(part);
            }
        }
    }

    /// <summary>Fetches exactly the entry's bytes of the package (a range request; redirects only to allowed hosts).</summary>
    private async Task ReceiveAsync(ZipEntryArchive entry, string packed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var address = entry.Url;
        for (var hop = 0; ; hop++)
        {
            ToolDownloader.Check(address);
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.Range = new RangeHeaderValue(entry.DataOffset, entry.DataOffset + entry.CompressedBytes - 1);
            using var headers = Reads(cancellationToken);
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The server did not answer within {_policy.ReadTimeout.TotalSeconds:0} s.");
            }

            using (response)
            {
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
                {
                    if (hop >= MaxRedirects)
                    {
                        throw new ToolInstallException("Too many redirects.") { Code = "download", Detail = "too many redirects" };
                    }

                    address = next.IsAbsoluteUri ? next : new Uri(address, next);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    _logger.LogWarning("{File} {Version}: the pinned package is gone (HTTP {Status})", entry.FileName, entry.Version, (int)response.StatusCode);
                    throw new ToolInstallException($"The pinned download is no longer available (HTTP {(int)response.StatusCode}).") { Code = "gone" };
                }

                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);
                }

                if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != entry.DataOffset
                    || (response.Content.Headers.ContentLength is { } length && length != entry.CompressedBytes))
                {
                    // Never the whole package instead (it may be 200 MB): the pin is the entry's bytes.
                    _logger.LogError("{File} {Version}: the server did not send the asked part of the package (HTTP {Status})", entry.FileName, entry.Version,
                        (int)response.StatusCode);
                    throw new ToolInstallException($"Download failed: the server did not send the asked part (HTTP {(int)response.StatusCode}).")
                    {
                        Code = "download",
                        Detail = "HTTP " + (int)response.StatusCode,
                    };
                }

                await CopyAsync(entry, response, packed, progress, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
    }

    private async Task CopyAsync(ZipEntryArchive entry, HttpResponseMessage response, string packed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[1 << 16];
        long done = 0;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(packed, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        using var reads = Reads(cancellationToken);
        while (done < entry.CompressedBytes)
        {
            int read;
            reads.CancelAfter(_policy.ReadTimeout);
            try
            {
                read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, entry.CompressedBytes - done)), reads.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"No data arrived for {_policy.ReadTimeout.TotalSeconds:0} s (after {done / 1_000_000} MB).");
            }

            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            done += read;
            progress?.Report(new ToolProgress(Math.Min(1, (double)done / entry.CompressedBytes), entry.FileName));
        }

        if (done < entry.CompressedBytes)
        {
            throw new IOException($"The connection ended after {done} of {entry.CompressedBytes} bytes.");
        }
    }

    /// <summary>Unpacks the received bytes into the part file; its size and SHA-256 (at most the pinned size plus one byte is written).</summary>
    private static async Task<(long Bytes, string Sha256)> UnpackAsync(ZipEntryArchive entry, string packed, string part, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 16];
        long bytes = 0;
        await using (var input = new FileStream(packed, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
        await using (var source = entry.Deflated ? new DeflateStream(input, CompressionMode.Decompress) : (Stream)input)
        await using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            int read;
            try
            {
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    bytes += read;
                    if (bytes > entry.Bytes)
                    {
                        // A wrong entry or a hostile server: never more than the pinned size on disk.
                        break;
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (InvalidDataException)
            {
                // Not deflate data at all: a hash mismatch.
                bytes = -1;
            }
        }

        return (bytes, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private CancellationTokenSource Reads(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(_policy.ReadTimeout);
        return source;
    }

    private static bool Transient(Exception ex) => ex switch
    {
        ToolInstallException => false,
        HttpRequestException http => http.StatusCode is null || (int)http.StatusCode >= 500 || http.StatusCode == HttpStatusCode.TooManyRequests,
        IOException or TimeoutException or AuthenticationException or SocketException => true,
        OperationCanceledException => true,
        _ => false,
    };
}
