namespace TMConcept.Core.Channels;

/// <summary>
/// A channel's public details read by yt-dlp (<see cref="ChannelListingReader.Read"/>): its name, description, the address of its photo (an
/// https thumbnail; null when none), its newest videos and, when the address was a channel's home that lists its tabs instead of videos, the
/// addresses of those tabs (read next).
/// </summary>
public sealed record ChannelListing(string Name, string Description, string? PhotoUrl, IReadOnlyList<ChannelVideo> Videos, IReadOnlyList<string> Tabs);
