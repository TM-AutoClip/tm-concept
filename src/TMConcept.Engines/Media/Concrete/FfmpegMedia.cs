using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Media;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Media;

/// <summary>ffmpeg work behind <see cref="IMediaTools"/>: mux (see <see cref="FfmpegMuxer"/>) and a thumbnail from the first frame that is not black.</summary>
public sealed class FfmpegMedia(FfmpegMuxer muxer, ToolInstaller tools, IProcessRunner runner, ILogger<FfmpegMedia> logger) : IMediaTools
{
    private static readonly double[] Moments = [0.6, 1.2, 2.5, 4, 6, 9];

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredComponents { get; } = [Core.Components.ComponentCodes.Ffmpeg];

    /// <inheritdoc />
    public Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, Core.Media.MuxOverlay overlay,
        IReadOnlyList<long> fadesMs, string output, CancellationToken cancellationToken) =>
        muxer.MuxAsync(folder, silentVideo, clips, overlay, fadesMs, output, cancellationToken);

    /// <summary>The ffprobe arguments that print the codec type of every stream and the file's length (pure; tests read them).</summary>
    public static IReadOnlyList<string> ProbeArguments(string file) =>
        ["-v", "error", "-show_entries", "stream=codec_type:format=duration", "-of", "default=noprint_wrappers=1", file];

    /// <summary>
    /// The length in milliseconds from ffprobe's lines (<see cref="ProbeArguments"/>): only when a stream is audio and the length is a positive
    /// number; <c>null</c> otherwise.
    /// </summary>
    public static long? ProbeDuration(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var audio = false;
        double? seconds = null;
        foreach (var line in lines.Select(l => l.Trim()))
        {
            if (line == "codec_type=audio")
            {
                audio = true;
            }
            else if (line.StartsWith("duration=", StringComparison.Ordinal)
                && double.TryParse(line.AsSpan("duration=".Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                seconds = value;
            }
        }

        return audio && seconds is { } s ? (long)Math.Round(s * 1000) : null;
    }

    /// <inheritdoc />
    public async Task<long?> AudioDurationAsync(string file, CancellationToken cancellationToken)
    {
        var ffprobe = tools.Sibling(ToolKind.Ffmpeg, "ffprobe");
        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        var lines = new List<string>();
        var result = await runner.RunAsync(new ProcessStart(ffprobe, [.. ProbeArguments(file)], folder), line =>
        {
            lock (lines)
            {
                lines.Add(line);
            }
        }, cancellationToken).ConfigureAwait(false);
        List<string> read;
        lock (lines)
        {
            read = [.. lines];
        }

        var duration = result.ExitCode == 0 ? ProbeDuration(read) : null;
        if (duration is null)
        {
            // The file name only: the folder is the user's.
            logger.LogError("ffprobe could not read the sound of {File} (exit {Code}): {Error}", Path.GetFileName(file), result.ExitCode,
                Core.Processes.ChildOutput.Summary(result.LastLines.Replace(folder, "<folder>", StringComparison.Ordinal)).Trim());
        }

        return duration;
    }

    /// <summary>
    /// ffmpeg's error lines without the user's folders (they name the files it read and wrote): the paths are replaced first and the text is
    /// cut after, so a cut never leaves part of a path.
    /// </summary>
    internal static string Paths(string text, string video, string jpeg, string folder) => Core.Processes.ChildOutput.Summary(text
            .Replace(video, Path.GetFileName(video), StringComparison.Ordinal)
            .Replace(jpeg, Path.GetFileName(jpeg), StringComparison.Ordinal)
            .Replace(folder, "<video folder>", StringComparison.Ordinal))
        .Trim();

    /// <summary>Width of a sampled region (<see cref="SampleAsync"/>): small, the measure is a mean.</summary>
    public const int SampleWidth = 96;

    /// <summary>Height of a sampled region.</summary>
    public const int SampleHeight = 48;

    /// <summary>
    /// The ffmpeg arguments of one sample: the frame at <paramref name="atMs"/> (fast seek), <paramref name="region"/> cut out and averaged
    /// down to <see cref="SampleWidth"/> × <see cref="SampleHeight"/> RGB pixels, written raw to <paramref name="output"/>.
    /// </summary>
    public static IReadOnlyList<string> SampleArguments(string file, long atMs, FrameRegion region, string output)
    {
        ArgumentNullException.ThrowIfNull(region);
        var c = CultureInfo.InvariantCulture;
        static double Share(double value) => Math.Clamp(value, 0, 1);
        var width = Math.Max(0.01, Share(region.Width));
        var height = Math.Max(0.01, Share(region.Height));
        var crop = string.Create(c, $"crop=w=iw*{width:0.####}:h=ih*{height:0.####}:x=iw*{Math.Min(Share(region.X), 1 - width):0.####}:y=ih*{Math.Min(Share(region.Y), 1 - height):0.####}");
        return ["-hide_banner", "-loglevel", "error", "-y", "-ss", (Math.Max(0, atMs) / 1000.0).ToString("0.###", c), "-i", file, "-frames:v", "1",
            "-vf", string.Create(c, $"{crop},scale={SampleWidth}:{SampleHeight}:flags=area,format=rgb24"), "-f", "rawvideo", output];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FramePixels?>> SampleAsync(string file, IReadOnlyList<long> atMs, FrameRegion region, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(atMs);
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        var output = Path.Combine(folder, ".sample-" + Guid.NewGuid().ToString("N") + ".rgb");
        var samples = new List<FramePixels?>();
        var size = SampleWidth * SampleHeight * 3;
        try
        {
            foreach (var at in atMs)
            {
                if (File.Exists(output))
                {
                    File.Delete(output);
                }

                var result = await runner.RunAsync(new ProcessStart(ffmpeg, [.. SampleArguments(file, at, region, output)], folder), null, cancellationToken)
                    .ConfigureAwait(false);
                if (result.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length < size)
                {
                    // Past the end of the video, or a file ffmpeg cannot read: this moment has no sample (the caller keeps the normal look).
                    logger.LogWarning("ffmpeg could not sample {File} at {Ms} ms (exit {Code}): {Error}", Path.GetFileName(file), at, result.ExitCode,
                        Core.Processes.ChildOutput.Summary(result.LastLines.Replace(folder, "<folder>", StringComparison.Ordinal)).Trim());
                    samples.Add(null);
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false);
                samples.Add(new FramePixels(SampleWidth, SampleHeight, bytes.AsMemory(0, size)));
            }

            return samples;
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> ThumbnailAsync(string video, string jpeg, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        var folder = Path.GetDirectoryName(jpeg)!;
        var probe = Path.Combine(folder, ".probe.gray");
        var error = string.Empty;
        var read = false;
        try
        {
            foreach (var moment in Moments)
            {
                var at = moment.ToString("0.###", CultureInfo.InvariantCulture);
                var result = await runner.RunAsync(new ProcessStart(ffmpeg,
                    ["-hide_banner", "-loglevel", "error", "-y", "-ss", at, "-i", video, "-frames:v", "1", "-vf", "scale=16:16,format=gray", "-f", "rawvideo", probe],
                    folder), null, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0 || !File.Exists(probe) || new FileInfo(probe).Length == 0)
                {
                    // Past the end of a short video, or ffmpeg cannot read it: logged when no moment could be read at all.
                    error = error.Length > 0 ? error
                        : string.Create(CultureInfo.InvariantCulture, $"exit {result.ExitCode}: {Paths(result.LastLines, video, jpeg, folder)}");
                    continue;
                }

                read = true;
                var pixels = await File.ReadAllBytesAsync(probe, cancellationToken).ConfigureAwait(false);
                if (pixels.Max() < 40 && pixels.Average(p => p) < 6)
                {
                    continue;
                }

                var shot = await runner.RunAsync(new ProcessStart(ffmpeg,
                    ["-hide_banner", "-loglevel", "error", "-y", "-ss", at, "-i", video, "-frames:v", "1", "-vf", "scale=480:-2", "-q:v", "3", jpeg], folder),
                    null, cancellationToken).ConfigureAwait(false);
                if (shot.ExitCode == 0 && File.Exists(jpeg))
                {
                    return true;
                }

                logger.LogError("ffmpeg could not write the thumbnail (exit {Code}): {Error}", shot.ExitCode, Paths(shot.LastLines, video, jpeg, folder));
                return false;
            }

            if (!read)
            {
                logger.LogError("ffmpeg could not read a frame for the thumbnail: {Error}", error);
            }
            else
            {
                logger.LogWarning("No frame that is not black was found for the thumbnail");
            }

            return false;
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
    }
}
