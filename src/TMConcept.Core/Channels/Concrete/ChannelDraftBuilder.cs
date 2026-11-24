using TMConcept.Core.Profiles;

namespace TMConcept.Core.Channels;

/// <summary>
/// The numbers of a draft profile from a channel's videos ("Kanaldan esinlen", 0.1.53): the target length is the median of the listed lengths,
/// rounded to 5 seconds and kept within <see cref="ProfileLimits.SecondsMin"/>–<see cref="ProfileLimits.SecondsMax"/>; the format is the shape
/// most of its videos have (taller than wide, a <c>/shorts/</c> address or a TikTok / Instagram video = vertical; wider than tall = horizontal);
/// the language is <see cref="ChannelLanguageGuess"/> of its titles. Each is null when the videos do not tell. Pure.
/// </summary>
public static class ChannelDraftBuilder
{
    private static readonly string[] VerticalHosts = ["tiktok.com", "instagram.com"];

    /// <summary>The facts of <paramref name="listing"/>.</summary>
    public static ChannelFacts Facts(ChannelListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        return new ChannelFacts(TargetSeconds(listing.Videos), Format(listing.Videos), ChannelLanguageGuess.Guess(listing.Videos.Select(v => v.Title)));
    }

    /// <summary>The target length of <paramref name="videos"/> (seconds), or null when no length is listed.</summary>
    public static int? TargetSeconds(IReadOnlyList<ChannelVideo> videos)
    {
        ArgumentNullException.ThrowIfNull(videos);
        var lengths = videos.Where(v => !v.Live && v.Seconds is > 0).Select(v => v.Seconds!.Value).ToList();
        if (lengths.Count == 0)
        {
            return null;
        }

        var median = TempoProfileMerger.Median(lengths);
        var rounded = (int)(Math.Round(median / 5, MidpointRounding.AwayFromZero) * 5);
        return Math.Clamp(rounded, ProfileLimits.SecondsMin, ProfileLimits.SecondsMax);
    }

    /// <summary>The format most of <paramref name="videos"/> have, or null when none tells (a tie is null too).</summary>
    public static VideoFormat? Format(IReadOnlyList<ChannelVideo> videos)
    {
        ArgumentNullException.ThrowIfNull(videos);
        var vertical = 0;
        var horizontal = 0;
        foreach (var video in videos)
        {
            switch (Shape(video))
            {
                case VideoFormat.Vertical:
                    vertical++;
                    break;
                case VideoFormat.Horizontal:
                    horizontal++;
                    break;
            }
        }

        return vertical == horizontal ? null : vertical > horizontal ? VideoFormat.Vertical : VideoFormat.Horizontal;
    }

    /// <summary>The shape of one video, or null when it does not tell.</summary>
    public static VideoFormat? Shape(ChannelVideo video)
    {
        ArgumentNullException.ThrowIfNull(video);
        if (video.Width > 0 && video.Height > 0 && video.Width != video.Height)
        {
            return video.Height > video.Width ? VideoFormat.Vertical : VideoFormat.Horizontal;
        }

        if (!Uri.TryCreate(video.Url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        return uri.AbsolutePath.Contains("/shorts/", StringComparison.OrdinalIgnoreCase)
            || VerticalHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal))
            ? VideoFormat.Vertical
            : null;
    }
}
