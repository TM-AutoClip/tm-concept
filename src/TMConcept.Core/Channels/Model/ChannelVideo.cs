namespace TMConcept.Core.Channels;

/// <summary>
/// One video of a channel as yt-dlp lists it (public details only): its id, address, title, length (null when not listed), the start of its
/// description, the upload date (null when not listed), its picture size (0 when not listed) and whether it is live or still to come.
/// </summary>
public sealed record ChannelVideo(string Id, string Url, string Title, double? Seconds, string Description, DateOnly? Uploaded, int Width, int Height,
    bool Live);
