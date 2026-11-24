namespace TMConcept.Core.Channels;

/// <summary>
/// Picks the sample videos of "Kanaldan esinlen" (0.1.53): at most the asked number (kept within <see cref="ChannelLimits.SamplesMin"/>–
/// <see cref="ChannelLimits.SamplesMax"/>), shorter ones first (less to download; only the first <see cref="References.ReferenceLimits.MaxSeconds"/>
/// is measured anyway), never a live or upcoming one, never one shorter than <see cref="ChannelLimits.SampleMinSeconds"/>; videos whose length
/// is not listed come last, in the channel's order. Each address once. Pure.
/// </summary>
public static class ChannelSamplePicker
{
    /// <summary>The asked number kept within the limits.</summary>
    public static int Clamp(int count) => Math.Clamp(count, ChannelLimits.SamplesMin, ChannelLimits.SamplesMax);

    /// <summary>The samples of <paramref name="videos"/> (the channel's order: newest first).</summary>
    public static IReadOnlyList<ChannelVideo> Pick(IReadOnlyList<ChannelVideo> videos, int count)
    {
        ArgumentNullException.ThrowIfNull(videos);
        var usable = videos.Select((v, i) => (Video: v, Order: i))
            .Where(x => !x.Video.Live && (x.Video.Seconds is null || x.Video.Seconds >= ChannelLimits.SampleMinSeconds))
            .DistinctBy(x => x.Video.Url, StringComparer.Ordinal)
            .ToList();
        return usable.Where(x => x.Video.Seconds is not null).OrderBy(x => x.Video.Seconds).ThenBy(x => x.Order)
            .Concat(usable.Where(x => x.Video.Seconds is null).OrderBy(x => x.Order))
            .Take(Clamp(count))
            .Select(x => x.Video)
            .ToList();
    }
}
