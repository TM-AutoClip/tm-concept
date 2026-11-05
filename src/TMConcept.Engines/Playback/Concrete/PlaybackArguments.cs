using System.Globalization;
using TMConcept.Core.Playback;

namespace TMConcept.Engines.Playback;

/// <summary>
/// ffmpeg arguments of the player. Both streams seek with <c>-ss</c> before the input: for a re-encoded output ffmpeg decodes from the
/// keyframe and drops everything before the position, so picture and sound start at the same instant (the sync starts right).
/// </summary>
public static class PlaybackArguments
{
    /// <summary>Picture: BGRA frames of exactly <paramref name="width"/>×<paramref name="height"/> (letterboxed) at <paramref name="fps"/>.</summary>
    public static IReadOnlyList<string> Video(string file, long startMs, int width, int height, int fps) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "error", "-ss", Seconds(startMs), "-i", file, "-an", "-sn",
        "-vf", Filter(width, height, fps), "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1",
    ];

    /// <summary>Sound: 16-bit stereo PCM at <see cref="PlaybackFormat.SampleRate"/>.</summary>
    public static IReadOnlyList<string> Audio(string file, long startMs) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "error", "-ss", Seconds(startMs), "-i", file, "-vn", "-sn",
        "-f", "s16le", "-acodec", "pcm_s16le", "-ar", PlaybackFormat.SampleRate.ToString(CultureInfo.InvariantCulture),
        "-ac", PlaybackFormat.Channels.ToString(CultureInfo.InvariantCulture), "pipe:1",
    ];

    /// <summary>One picture at <paramref name="atMs"/> (paused seek, first picture).</summary>
    public static IReadOnlyList<string> Frame(string file, long atMs, int width, int height) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "error", "-ss", Seconds(atMs), "-i", file, "-an", "-sn",
        "-vf", Filter(width, height, null), "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1",
    ];

    /// <summary>Seconds with millisecond precision, invariant culture (a comma would break the argument).</summary>
    public static string Seconds(long milliseconds) => (Math.Max(0, milliseconds) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Filter(int width, int height, int? fps) => string.Create(CultureInfo.InvariantCulture,
        $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2{(fps is { } f ? ",fps=" + f : string.Empty)}");
}
