using System.Globalization;

namespace TMConcept.Core.Playback;

/// <summary>Time text of the player ("0:07", "2:59", "1:02:03").</summary>
public static class PlaybackTime
{
    /// <summary>Formats milliseconds (negative counts as zero).</summary>
    public static string Format(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
