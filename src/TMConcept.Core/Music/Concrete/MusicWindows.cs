namespace TMConcept.Core.Music;

/// <summary>
/// Where the music plays (0.1.45; pure): the first scene whose hint says "start" (the first scene when none does) to the last scene after it
/// whose hint says "end" (the last scene when none does). Its start fades in when that scene's hint says "fade"; an end before the video's
/// last scene fades out when its hint says "fade".
/// </summary>
public static class MusicWindows
{
    /// <summary>The window of a video of <paramref name="scenes"/> scenes from each scene's hint (null = no hint).</summary>
    public static MusicWindow Of(IReadOnlyList<MusicHint?> hints, int scenes)
    {
        ArgumentNullException.ThrowIfNull(hints);
        if (scenes <= 0)
        {
            return new MusicWindow(0, 0, false, false);
        }

        var first = 0;
        for (var i = 0; i < Math.Min(hints.Count, scenes); i++)
        {
            if (hints[i] is { Start: true })
            {
                first = i;
                break;
            }
        }

        var last = scenes - 1;
        for (var i = Math.Min(hints.Count, scenes) - 1; i >= first; i--)
        {
            if (hints[i] is { End: true })
            {
                last = i;
                break;
            }
        }

        var fadeIn = first < hints.Count && hints[first] is { } open && open.Transition == MusicTags.Fade;
        var fadeOut = last < scenes - 1 && last < hints.Count && hints[last] is { } close && close.Transition == MusicTags.Fade;
        return new MusicWindow(first, last, fadeIn, fadeOut);
    }
}
