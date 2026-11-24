namespace TMConcept.Core.Channels;

/// <summary>
/// Fetches a channel's photo (its public thumbnail, "Kanaldan esinlen", 0.1.53) into the look's temporary folder: https only, an image kind
/// only, at most <see cref="ChannelLimits.PhotoMaxBytes"/>. It becomes the profile's photo only when the user keeps it.
/// </summary>
public interface IChannelPhotoFetcher
{
    /// <summary>The fetched file in <paramref name="folder"/>; throws <see cref="HttpRequestException"/> or <see cref="InvalidOperationException"/>.</summary>
    Task<string> FetchAsync(Uri photo, string folder, CancellationToken cancellationToken);
}
