using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>
/// Downloads a big pinned file (local model weights of several GB) that survives a broken connection: the same address rules as
/// <see cref="ToolDownloader"/> (HTTPS, redirects only to allowed hosts, SHA-256 checked before the file is moved into place), plus
/// <list type="bullet">
/// <item>retries: a dropped connection, a TLS failure, a time-out, HTTP 408/429/5xx are tried again up to <see cref="Attempts"/> times with a
/// growing pause (<see cref="FirstDelay"/>, doubled each time), each attempt resolving the redirects again (Hugging Face's signed file
/// addresses expire);</item>
/// <item>resume: what arrived stays in <c>&lt;file&gt;.part</c> and the next attempt of the same download asks only for the rest
/// (<c>Range: bytes=n-</c>); a server that answers with the whole file starts it again from 0; the part is hashed first so the SHA-256 covers
/// the whole file (28 Nov 2026: a later install, or the next start after a cancel or a close, deletes the half-finished component's files
/// first and downloads it again);</item>
/// <item>no time limit for the whole transfer (0.1.16 had 30 minutes: a 12 GB file needs about 7 MB/s), only for a read that brings
/// nothing for <see cref="IdleTimeout"/>.</item>
/// </list>
/// Not retried: a gone file (404/410), another HTTP error, a host that is not allowed, a SHA-256 mismatch (the part is deleted). Used for
/// the local image models' weights; reusable for any <see cref="IArchiveDownloader"/> caller.
/// </summary>
public sealed class ResumingDownloader(DownloadTransport transport, ILogger<ResumingDownloader> logger) : IArchiveDownloader, IDisposable
{
    private const int MaxRedirects = 5;
    private readonly HttpClient _http = new(transport.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Attempts per file (the first one included).</summary>
    public int Attempts { get; init; } = 6;

    /// <summary>The pause after the first failed attempt (doubled after each further one, at most a minute).</summary>
    public TimeSpan FirstDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long one read may bring nothing before the attempt counts as broken.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <inheritdoc />
    public async Task DownloadAsync(ToolArchive archive, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var part = file + ".part";
        var delay = FirstDelay;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await AttemptAsync(archive, file, part, progress, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (Transient(ex, cancellationToken) && attempt < Attempts)
            {
                logger.LogWarning("{File}: attempt {Attempt} of {Attempts} broke off ({Error}); trying again in {Seconds:0} s from {Bytes} bytes", archive.ExecutablePath,
                    attempt, Attempts, ex.Message, delay.TotalSeconds, File.Exists(part) ? new FileInfo(part).Length : 0);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMinutes(1).Ticks));
            }
            catch (Exception ex) when (Transient(ex, cancellationToken))
            {
                logger.LogError(ex, "{File}: the download broke off {Attempts} times; what arrived is kept for the next try", archive.ExecutablePath, Attempts);
                throw new ToolInstallException($"The download could not be completed after {Attempts} attempts: {Message(ex)}", ex) { Code = "download" };
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>Whether a failure is worth another attempt (never the user's own cancel).</summary>
    private static bool Transient(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        _ when cancellationToken.IsCancellationRequested => false,
        RetryableException => true,
        HttpRequestException => true,
        IOException => true,
        OperationCanceledException => true,
        _ => false,
    };

    private static string Message(Exception ex) => ex.InnerException is { } inner && ex is HttpRequestException ? ex.Message + " " + inner.Message : ex.Message;

    private async Task AttemptAsync(ToolArchive archive, string file, string part, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (archive.ApproximateBytes > 0 && have > archive.ApproximateBytes)
        {
            File.Delete(part);
            have = 0;
        }

        var address = archive.Url;
        for (var hop = 0; ; hop++)
        {
            ToolDownloader.Check(address);
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            if (have > 0)
            {
                request.Headers.Range = new RangeHeaderValue(have, null);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            {
                if (hop >= MaxRedirects)
                {
                    throw new ToolInstallException("Too many redirects.") { Code = "download" };
                }

                address = next.IsAbsoluteUri ? next : new Uri(address, next);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                logger.LogWarning("{File}: the pinned download is gone (HTTP {Status})", archive.ExecutablePath, (int)response.StatusCode);
                throw new ToolInstallException($"The pinned download is no longer available (HTTP {(int)response.StatusCode}).") { Code = "gone" };
            }

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && have > 0)
            {
                // Everything may already be there: the hash decides (a wrong part is deleted and the next attempt starts from 0).
                await FinishAsync(archive, file, part, null, have, cancellationToken).ConfigureAwait(false);
                return;
            }

            if ((int)response.StatusCode is 408 or 429 or >= 500)
            {
                throw new RetryableException($"HTTP {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ToolInstallException($"Download failed: HTTP {(int)response.StatusCode}.") { Code = "download" };
            }

            var resumed = response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From == have;
            if (!resumed)
            {
                have = 0;
            }

            long? end = response.Content.Headers.ContentLength is { } length ? have + length : null;
            var total = end ?? Math.Max(have, archive.ApproximateBytes);
            var resumedFrom = have;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if (have > 0)
            {
                await HashAsync(part, hash, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("{File}: resumed at {Bytes} of {Total} bytes", archive.ExecutablePath, have, total);
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                var done = have;
                while (true)
                {
                    int read;
                    using (var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        idle.CancelAfter(IdleTimeout);
                        try
                        {
                            read = await source.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            throw new RetryableException($"no data for {IdleTimeout.TotalSeconds:0} s");
                        }
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    progress?.Report(new ToolProgress(total > 0 ? Math.Min(1, (double)done / total) : 0, archive.ExecutablePath));
                }
            }

            if (end is { } expected && new FileInfo(part).Length < expected)
            {
                // The connection ended early without an error: the rest comes with the next attempt.
                throw new RetryableException("the connection ended before the whole file arrived");
            }

            await FinishAsync(archive, file, part, hash, resumedFrom, cancellationToken).ConfigureAwait(false);
            return;
        }
    }

    /// <summary>
    /// Checks the SHA-256 (of <paramref name="hash"/>, or of the part file when null) and moves the part into place, or deletes it. A
    /// mismatch of a file that was resumed (<paramref name="have"/> bytes were kept from before) is tried once more from 0: the kept part may
    /// have been damaged; a mismatch of a whole fresh download is final.
    /// </summary>
    private async Task FinishAsync(ToolArchive archive, string file, string part, IncrementalHash? hash, long have, CancellationToken cancellationToken)
    {
        string actual;
        if (hash is null)
        {
            using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await HashAsync(part, whole, cancellationToken).ConfigureAwait(false);
            actual = Convert.ToHexStringLower(whole.GetHashAndReset());
        }
        else
        {
            actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        if (!string.Equals(actual, archive.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            logger.LogError("{File}: SHA-256 mismatch (expected {Expected}, got {Actual}, {Bytes} bytes kept before); the file was deleted", archive.ExecutablePath,
                archive.Sha256, actual, have);
            if (hash is null || have > 0)
            {
                throw new RetryableException("the kept part did not match its SHA-256");
            }

            throw new ToolInstallException("The downloaded file does not match its pinned SHA-256; it was deleted.") { Code = "hash" };
        }

        File.Move(part, file, overwrite: true);
        logger.LogInformation("{File} downloaded and verified", archive.ExecutablePath);
    }

    private static async Task HashAsync(string path, IncrementalHash hash, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        var buffer = new byte[1 << 20];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
    }

    /// <summary>A failure that another attempt may cure (a server error, an early end, a stalled read).</summary>
    private sealed class RetryableException(string message) : IOException(message);
}
