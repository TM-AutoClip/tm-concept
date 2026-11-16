using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>
/// Downloads a pinned file over HTTPS, follows redirects only to allowed hosts (every hop is checked), hashes while writing, and deletes the
/// file when the SHA-256 does not match. Nothing is unpacked before the hash matches.
/// <para>
/// A bad network does not end the install at the first error (0.1.16 did, and every failure started a 12 GB model from zero): each file gets
/// <see cref="DownloadPolicy.Attempts"/> tries with a growing pause; the received part is kept in <c>&lt;file&gt;.part</c> and the next try
/// (also the next install, days later) continues from its end with an HTTP Range request (its bytes are hashed again first, so the final
/// check covers the whole file; a server that ignores the range sends the whole file and the part starts again). A try ends when no data
/// arrives for <see cref="DownloadPolicy.ReadTimeout"/>, never because the whole file takes long. What finally failed says why
/// (<see cref="ToolInstallException.Code"/> "tls", "dns", "connection", "timeout" with the system's own reason in
/// <see cref="ToolInstallException.Detail"/>), or "disk" when the drive is full (checked before the first try, and not retried: it is not the
/// network). A kept part that does not fit (the server refuses the range, or the whole file then fails its SHA-256) is checked as a whole file
/// first, then downloaded once more from byte 0 before a mismatch counts. A cancel by the user stays an <see cref="OperationCanceledException"/>,
/// a time-out never is one.
/// </para>
/// </summary>
public sealed class ToolDownloader : IArchiveDownloader, IDisposable
{
    private const int MaxRedirects = 5;
    private const long SpareBytes = 64L * 1024 * 1024;

    private readonly ILogger<ToolDownloader> _logger;
    private readonly DownloadPolicy _policy;
    private readonly HttpClient _http;

    /// <summary>Creates; <paramref name="policy"/> null = <see cref="DownloadPolicy.Default"/>.</summary>
    public ToolDownloader(DownloadTransport transport, ILogger<ToolDownloader> logger, DownloadPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _logger = logger;
        _policy = policy ?? DownloadPolicy.Default;

        // No time limit for the whole transfer: each read has its own (DownloadPolicy.ReadTimeout).
        _http = new HttpClient(transport.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Downloads <paramref name="archive"/> to <paramref name="file"/>.</summary>
    public async Task DownloadAsync(ToolArchive archive, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrEmpty(file);
        var part = file + ".part";
        CheckSpace(archive, file, part);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await AttemptAsync(archive, file, part, progress, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The user's cancel: the part stays for the next install.
                _logger.LogInformation("{Tool} {Version}: download cancelled; the part received so far is kept", archive.Tool, archive.Version);
                throw;
            }
            catch (IOException ex) when (DiskFull(ex))
            {
                // Another try cannot help, and it is not the network: the user is told the disk is full.
                _logger.LogError(ex, "{Tool} {Version}: the disk is full; the part received so far is kept", archive.Tool, archive.Version);
                throw new ToolInstallException("The disk is full.", ex) { Code = "disk", Detail = ex.Message.Trim() };
            }
            catch (Exception ex) when (Transient(ex))
            {
                var failure = Classify(ex, attempt);
                if (attempt >= _policy.Attempts)
                {
                    _logger.LogError(ex, "{Tool} {Version}: the download failed after {Attempts} tries ({Code}: {Detail}); the part received so far is kept",
                        archive.Tool, archive.Version, attempt, failure.Code, failure.Detail);
                    throw failure;
                }

                var delay = _policy.DelayAfter(attempt);
                _logger.LogWarning(ex, "{Tool} {Version}: try {Attempt} of {Attempts} failed ({Code}: {Detail}); trying again in {Seconds:0.#} s from byte {Bytes}",
                    archive.Tool, archive.Version, attempt, _policy.Attempts, failure.Code, failure.Detail, delay.TotalSeconds, Length(part));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>One try: continues the part (or starts it), follows the redirects, hashes everything, moves the whole file into place.</summary>
    private async Task AttemptAsync(ToolArchive archive, string file, string part, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var address = archive.Url;
        var restarted = false;
        for (var hop = 0; ; hop++)
        {
            Check(address);
            var have = Length(part);
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            if (have > 0)
            {
                request.Headers.Range = new RangeHeaderValue(have, null);
            }

            using var headers = Reads(cancellationToken);
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);

                // The headers came: the body has its own time-out for each read.
                headers.CancelAfter(Timeout.InfiniteTimeSpan);
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
                    // The pinned release was removed: a retry cannot help, the caller may fall back (ffmpeg from PATH).
                    _logger.LogWarning("{Tool} {Version}: the pinned download is gone (HTTP {Status})", archive.Tool, archive.Version, (int)response.StatusCode);
                    throw new ToolInstallException($"The pinned download is no longer available (HTTP {(int)response.StatusCode}).") { Code = "gone" };
                }

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && have > 0 && !restarted)
                {
                    // The whole file may already be there (the app ended before moving it into place): the hash decides.
                    if (await PartMatchesAsync(archive, part, cancellationToken).ConfigureAwait(false))
                    {
                        File.Move(part, file, overwrite: true);
                        _logger.LogInformation("{Tool} {Version}: the kept part was the whole file; verified", archive.Tool, archive.Version);
                        return;
                    }

                    // The part is not a beginning of this file (or already longer): start it again, at once.
                    _logger.LogWarning("{Tool} {Version}: the server refused to continue at byte {Bytes}; the part starts again", archive.Tool, archive.Version, have);
                    File.Delete(part);
                    restarted = true;
                    address = archive.Url;
                    hop = -1;
                    continue;
                }

                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // A busy or broken server: worth another try.
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new ToolInstallException($"Download failed: HTTP {(int)response.StatusCode}.")
                    {
                        Code = "download",
                        Detail = "HTTP " + (int)response.StatusCode,
                    };
                }

                var resume = have > 0 && response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From == have;
                if (have > 0 && !resume)
                {
                    _logger.LogInformation("{Tool} {Version}: the server sends the whole file again; the part starts again", archive.Tool, archive.Version);
                    have = 0;
                }
                else if (resume)
                {
                    _logger.LogInformation("{Tool} {Version}: continuing the download at byte {Bytes}", archive.Tool, archive.Version, have);
                }

                if (!await ReceiveAsync(archive, part, have, response, progress, cancellationToken).ConfigureAwait(false))
                {
                    if (have > 0 && !restarted)
                    {
                        // The kept part was not the beginning of this file (an older pin of the same name, a write cut short): once more
                        // from byte 0 before the mismatch counts.
                        _logger.LogWarning("{Tool} {Version}: the continued file did not match its SHA-256; downloading it again from the start",
                            archive.Tool, archive.Version);
                        restarted = true;
                        address = archive.Url;
                        hop = -1;
                        continue;
                    }

                    throw new ToolInstallException("The downloaded file does not match its pinned SHA-256; it was deleted.") { Code = "hash" };
                }
            }

            File.Move(part, file, overwrite: true);
            _logger.LogInformation("{Tool} {Version} downloaded and verified", archive.Tool, archive.Version);
            return;
        }
    }

    /// <summary>
    /// Writes the body after the <paramref name="have"/> bytes already in the part, then checks the hash of the whole part: false (the part
    /// deleted) when it does not match.
    /// </summary>
    private async Task<bool> ReceiveAsync(ToolArchive archive, string part, long have, HttpResponseMessage response, IProgress<ToolProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 16];
        if (have > 0)
        {
            // The kept bytes are hashed again: the final check covers the whole file, not only what came now.
            await using var kept = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            int count;
            while ((count = await kept.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, count);
            }
        }

        var total = response.Content.Headers.ContentLength is { } length ? have + length : archive.ApproximateBytes;
        var name = archive.Url.Segments[^1];
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            using var reads = Reads(cancellationToken);
            var done = have;
            while (true)
            {
                int read;
                reads.CancelAfter(_policy.ReadTimeout);
                try
                {
                    read = await source.ReadAsync(buffer, reads.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await target.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                    throw new TimeoutException($"No data arrived for {_policy.ReadTimeout.TotalSeconds:0} s (after {done / 1_000_000} MB).");
                }

                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                progress?.Report(new ToolProgress(total > 0 ? Math.Min(1, (double)done / total) : 0, name));
            }

            if (response.Content.Headers.ContentLength is { } expected && done - have < expected)
            {
                // The connection ended before the promised length: the part is kept, the next try continues it.
                throw new IOException($"The connection ended after {done - have} of {expected} bytes.");
            }
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(actual, archive.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            _logger.LogError("{Tool} {Version}: SHA-256 mismatch (expected {Expected}, got {Actual}, {Bytes} bytes kept from before); the file was deleted",
                archive.Tool, archive.Version, archive.Sha256, actual, have);
            return false;
        }

        return true;
    }

    /// <summary>Whether the part as it is has the pinned SHA-256 (a whole file that was never moved into place).</summary>
    private static async Task<bool> PartMatchesAsync(ToolArchive archive, string part, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        return string.Equals(actual, archive.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Before the first try: refuses at once ("disk") when the drive cannot hold what is still to come (the file's size less the part already
    /// there, plus 64 MB to spare); an unknown size or a drive that cannot be asked is not held against it.
    /// </summary>
    private void CheckSpace(ToolArchive archive, string file, string part)
    {
        var missing = archive.ApproximateBytes - Length(part);
        if (missing <= 0)
        {
            return;
        }

        long free;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(file));
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            free = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (free < missing + SpareBytes)
        {
            _logger.LogError("{Tool} {Version}: {Missing} MB to download but only {Free} MB free on the disk", archive.Tool, archive.Version, missing / 1_000_000,
                free / 1_000_000);
            throw new ToolInstallException($"Not enough free disk space: {missing / 1_000_000} MB needed, {free / 1_000_000} MB free.")
            {
                Code = "disk",
                Detail = $"{(missing + SpareBytes) / 1_000_000} MB needed, {free / 1_000_000} MB free",
            };
        }
    }

    /// <summary>Whether an I/O error means the disk is full (Windows ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL, Linux and macOS ENOSPC).</summary>
    internal static bool DiskFull(IOException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        const int DiskFullWindows = unchecked((int)0x80070070);
        const int HandleDiskFullWindows = unchecked((int)0x80070027);
        const int NoSpace = 28;
        return ex.HResult is DiskFullWindows or HandleDiskFullWindows or NoSpace
            || ex.Message.Contains("No space left on device", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A token for one wait on the network: the caller's, plus the read time-out (armed with CancelAfter).</summary>
    private CancellationTokenSource Reads(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(_policy.ReadTimeout);
        return source;
    }

    /// <summary>Whether another try can help: network and server errors, time-outs; never a hash, host or "gone" refusal.</summary>
    private static bool Transient(Exception ex) => ex switch
    {
        ToolInstallException => false,
        HttpRequestException http => http.StatusCode is null || (int)http.StatusCode >= 500 || http.StatusCode == HttpStatusCode.TooManyRequests,
        IOException or TimeoutException or AuthenticationException or SocketException => true,
        OperationCanceledException => true,
        _ => false,
    };

    /// <summary>The failure as the user is told it: what kind of network problem, and the system's own reason.</summary>
    public static ToolInstallException Classify(Exception ex, int attempts)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var inner = Innermost(ex);
        var detail = inner.Message.Trim();
        var code = Kind(ex);
        var message = $"The download could not be completed after {attempts} {(attempts == 1 ? "try" : "tries")}: {ex.Message}"
            + (ReferenceEquals(inner, ex) ? string.Empty : $" ({detail})");
        return new ToolInstallException(message, ex) { Code = code, Detail = detail };
    }

    /// <summary>The kind of a network failure (from .NET's own classification first, then the inner exceptions).</summary>
    private static string Kind(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case TimeoutException:
                case OperationCanceledException:
                    return "timeout";
                case AuthenticationException:
                    return "tls";
                case HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError }:
                    return "tls";
                case HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError }:
                    return "dns";
                case SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData }:
                    return "dns";
                case SocketException { SocketErrorCode: SocketError.TimedOut }:
                    return "timeout";
            }
        }

        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is HttpRequestException { StatusCode: not null })
            {
                return "download";
            }

            if (e is HttpRequestException or HttpIOException or IOException or SocketException)
            {
                return "connection";
            }
        }

        return "download";
    }

    private static Exception Innermost(Exception ex)
    {
        var e = ex;
        while (e.InnerException is not null)
        {
            e = e.InnerException;
        }

        return e;
    }

    private static long Length(string file) => File.Exists(file) ? new FileInfo(file).Length : 0;

    internal static void Check(Uri address)
    {
        if (address.Scheme != Uri.UriSchemeHttps || !(ToolManifest.AllowedHosts.Contains(address.Host, StringComparer.OrdinalIgnoreCase)
            || ToolManifest.AllowedHostSuffixes.Any(s => address.Host.EndsWith(s, StringComparison.OrdinalIgnoreCase))))
        {
            throw new ToolInstallException($"Downloads from {address.Host} are not allowed.") { Code = "host" };
        }
    }
}
