using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>
/// Downloads a pinned archive over HTTPS, follows redirects only to allowed hosts, hashes while writing, and deletes the file when the
/// SHA-256 does not match. Nothing is unpacked before the hash matches.
/// </summary>
public sealed class ToolDownloader(DownloadTransport transport, ILogger<ToolDownloader> logger) : IArchiveDownloader, IDisposable
{
    private const int MaxRedirects = 5;
    private readonly HttpClient _http = new(transport.CreateHandler(), disposeHandler: true) { Timeout = TimeSpan.FromMinutes(30) };

    /// <summary>Downloads <paramref name="archive"/> to <paramref name="file"/>.</summary>
    public async Task DownloadAsync(ToolArchive archive, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var part = file + ".part";
        var address = archive.Url;
        try
        {
            for (var hop = 0; ; hop++)
            {
                Check(address);
                using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
                {
                    if (hop >= MaxRedirects)
                    {
                        throw new ToolInstallException("Too many redirects.") { Code = "download" };
                    }

                    address = next.IsAbsoluteUri ? next : new Uri(address, next);
                    continue;
                }

                if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
                {
                    // The pinned release was removed: a retry cannot help, the caller may fall back (ffmpeg from PATH).
                    logger.LogWarning("{Tool} {Version}: the pinned download is gone (HTTP {Status})", archive.Tool, archive.Version, (int)response.StatusCode);
                    throw new ToolInstallException($"The pinned download is no longer available (HTTP {(int)response.StatusCode}).") { Code = "gone" };
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new ToolInstallException($"Download failed: HTTP {(int)response.StatusCode}.") { Code = "download" };
                }

                var total = response.Content.Headers.ContentLength ?? archive.ApproximateBytes;
                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[1 << 16];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        done += read;
                        progress?.Report(new ToolProgress(total > 0 ? Math.Min(1, (double)done / total) : 0, archive.Url.Segments[^1]));
                    }

                    var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
                    if (!string.Equals(actual, archive.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogError("{Tool} {Version}: SHA-256 mismatch (expected {Expected}, got {Actual}); the file was deleted",
                            archive.Tool, archive.Version, archive.Sha256, actual);
                        throw new ToolInstallException("The downloaded file does not match its pinned SHA-256; it was deleted.") { Code = "hash" };
                    }
                }

                File.Move(part, file, overwrite: true);
                logger.LogInformation("{Tool} {Version} downloaded and verified", archive.Tool, archive.Version);
                return;
            }
        }
        catch (HttpRequestException ex)
        {
            throw new ToolInstallException("The download could not be completed: " + ex.Message, ex) { Code = "download" };
        }
        finally
        {
            if (File.Exists(part))
            {
                File.Delete(part);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private static void Check(Uri address)
    {
        if (address.Scheme != Uri.UriSchemeHttps || !ToolManifest.AllowedHosts.Contains(address.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new ToolInstallException($"Downloads from {address.Host} are not allowed.") { Code = "host" };
        }
    }
}
