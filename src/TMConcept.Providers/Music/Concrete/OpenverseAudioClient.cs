using System.Net;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Music;
using TMConcept.Core.Paths;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Music;

/// <summary>
/// The Openverse audio search (0.1.45, S6): no key, no account. A page is two anonymous GET requests — tracks whose licence allows commercial use
/// with changes (<c>license_type=commercial,modification</c>) and CC0 tracks (<c>license=cc0</c>) — merged without doubles and checked again
/// by <see cref="OpenverseAudioReader"/> (CC0, CC BY, CC BY-SA only). A picked track is downloaded over HTTPS into the given folder (at most
/// <see cref="OpenverseRules.MaxTrackBytes"/>, an audio file kind only); nothing else is sent. Only the search words leave the computer.
/// </summary>
public sealed class OpenverseAudioClient(ProviderTransport transport, ILogger<OpenverseAudioClient> logger) : IMusicSearch, IDisposable
{
    private readonly HttpClient _http = Create(transport);

    /// <inheritdoc />
    public async Task<MusicSearchPage> SearchAsync(string query, int page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var open = await GetAsync(OpenverseAudioReader.Address(query, page, "license_type=commercial,modification"), cancellationToken).ConfigureAwait(false);
        var free = await GetAsync(OpenverseAudioReader.Address(query, page, "license=cc0"), cancellationToken).ConfigureAwait(false);
        var tracks = open.Tracks.Concat(free.Tracks).GroupBy(t => t.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        logger.LogInformation("Openverse search page {Page}: {Count} usable tracks, {Dropped} left out for their licence", page, tracks.Count,
            open.Dropped + free.Dropped);
        return new MusicSearchPage(tracks, page, Math.Max(open.PageCount, free.PageCount), open.Dropped + free.Dropped);
    }

    /// <inheritdoc />
    public async Task<string> DownloadAsync(MusicTrack track, string folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (!track.Id.StartsWith(MusicLibraryRules.OpenversePrefix, StringComparison.Ordinal) || !MusicLicenses.Allowed(LicenseCode(track.License))
            || !Uri.TryCreate(track.DownloadUrl, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("This track cannot be downloaded.");
        }

        var extension = Path.GetExtension(address.AbsolutePath).ToLowerInvariant();
        if (!OpenverseRules.Extensions.Contains(extension, StringComparer.Ordinal))
        {
            extension = ".mp3";
        }

        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, LibraryPaths.SafeName(track.Id[MusicLibraryRules.OpenversePrefix.Length..], "track") + extension);
        var part = file + ".part";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OpenverseRules.Timeout * 5);
        using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && final.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("The track's address left HTTPS.");
        }

        if (response.Content.Headers.ContentLength is > OpenverseRules.MaxTrackBytes)
        {
            throw new InvalidOperationException("The track is larger than the app takes.");
        }

        try
        {
            await using (var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > OpenverseRules.MaxTrackBytes)
                    {
                        throw new InvalidOperationException("The track is larger than the app takes.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                }
            }

            File.Move(part, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(part))
            {
                File.Delete(part);
            }
        }

        logger.LogInformation("Openverse track downloaded: {Id} ({License})", track.Id, track.License);
        return file;
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>The Openverse code of a licence label ("CC BY-SA 4.0" → "by-sa", "CC0 1.0" → "cc0").</summary>
    public static string LicenseCode(string label)
    {
        var words = (label ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return string.Empty;
        }

        if (words[0].Equals("CC0", StringComparison.OrdinalIgnoreCase))
        {
            return "cc0";
        }

        return words.Length > 1 && words[0].Equals("CC", StringComparison.OrdinalIgnoreCase) ? words[1].ToLowerInvariant() : string.Empty;
    }

    private async Task<MusicSearchPage> GetAsync(Uri address, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OpenverseRules.Timeout);
        using var response = await _http.GetAsync(address, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("Openverse asks to wait before the next search.", null, response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        return OpenverseAudioReader.Read(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
    }

    private static HttpClient Create(ProviderTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var http = new HttpClient(transport.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TMConcept/1.0");
        return http;
    }
}
