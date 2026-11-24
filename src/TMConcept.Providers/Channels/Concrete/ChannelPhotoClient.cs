using Microsoft.Extensions.Logging;
using TMConcept.Core.Channels;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Channels;

/// <summary>
/// <see cref="IChannelPhotoFetcher"/> ("Kanaldan esinlen", 0.1.53): one anonymous GET of the channel's public thumbnail over HTTPS (a redirect
/// that leaves HTTPS is refused), an image kind only (PNG, JPEG, WebP by the answer's type), at most <see cref="ChannelLimits.PhotoMaxBytes"/>,
/// into the look's temporary folder. Nothing is sent but the request itself; the address is never logged.
/// </summary>
public sealed class ChannelPhotoClient(ProviderTransport transport, ILogger<ChannelPhotoClient> logger) : IChannelPhotoFetcher, IDisposable
{
    // The longest a photo may take.
    private static readonly TimeSpan PhotoTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http = Create(transport);

    /// <summary>The file extension of an image answer's type (<c>image/png</c> → <c>.png</c>), or null when it is not an image kind the app reads.</summary>
    public static string? Extension(string? mediaType) => (mediaType ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/webp" => ".webp",
        _ => null,
    };

    /// <inheritdoc />
    public async Task<string> FetchAsync(Uri photo, string folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (photo.Scheme != Uri.UriSchemeHttps || photo.UserInfo.Length > 0)
        {
            throw new InvalidOperationException("The channel photo is not an https address.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PhotoTimeout);
        using var response = await _http.GetAsync(photo, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && final.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("The channel photo's address left HTTPS.");
        }

        var extension = Extension(response.Content.Headers.ContentType?.MediaType)
            ?? throw new InvalidOperationException("The channel photo is not a PNG, JPEG or WebP image.");
        if (response.Content.Headers.ContentLength is > ChannelLimits.PhotoMaxBytes)
        {
            throw new InvalidOperationException("The channel photo is too large.");
        }

        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "photo" + extension);
        await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
        await using (var target = File.Create(file))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > ChannelLimits.PhotoMaxBytes)
                {
                    throw new InvalidOperationException("The channel photo is too large.");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
            }
        }

        logger.LogInformation("Channel photo fetched ({Kilobytes:0} KB)", new FileInfo(file).Length / 1024.0);
        return file;
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private static HttpClient Create(ProviderTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var http = new HttpClient(transport.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TMConcept/1.0");
        return http;
    }
}
