namespace TMConcept.Core.Channels;

/// <summary>
/// Reads a channel's public details ("Kanaldan esinlen", 0.1.53; yt-dlp, the same component as the reference video, installed only after the
/// user's yes): the channel's name, description and photo address and its newest videos' titles, lengths, the start of their descriptions and
/// upload dates. Nothing is downloaded; no cookies, no sign-in, no API key.
/// </summary>
public interface IChannelLister
{
    /// <summary>yt-dlp is installed.</summary>
    bool Ready { get; }

    /// <summary>
    /// The public details of <paramref name="address"/> with at most <paramref name="count"/> videos; <paramref name="folder"/> is the look's
    /// own empty folder (yt-dlp runs there). Throws <see cref="ChannelInspireException"/> ("missing", "list").
    /// </summary>
    Task<ChannelListing> ListAsync(Uri address, int count, string folder, CancellationToken cancellationToken);
}
