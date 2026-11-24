using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Core.References;
using TMConcept.Core.Tools;
using TMConcept.Core.Videos;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Media;

/// <summary>
/// ffmpeg behind <see cref="IReferenceVideoReader"/> (S1, 0.1.52): the app's own ffmpeg writes the first <see cref="ReferenceLimits.MaxSeconds"/>
/// of a reference as raw grey frames (<see cref="MotionCheckLimits.FrameWidth"/> × <see cref="MotionCheckLimits.FrameHeight"/> at
/// <see cref="ReferenceLimits.FrameFps"/>) or small RGB frames (<see cref="ReferenceLimits.ColorWidth"/> × <see cref="ReferenceLimits.ColorHeight"/>
/// at <see cref="ReferenceLimits.ColorFps"/>) into a temporary file of the measure's folder, which is read one frame at a time and deleted.
/// The process runner starts ffmpeg at below-normal priority; the processor only.
/// </summary>
public sealed class FfmpegReferenceVideo(ToolInstaller tools, IProcessRunner runner, ILogger<FfmpegReferenceVideo> logger) : IReferenceVideoReader
{
    /// <summary>The ffmpeg arguments that write the grey frames of <paramref name="video"/> into <paramref name="output"/> (pure).</summary>
    public static IReadOnlyList<string> FrameArguments(string video, string output) =>
        ["-hide_banner", "-loglevel", "error", "-y", "-t", ReferenceLimits.MaxSeconds.ToString(CultureInfo.InvariantCulture), "-i", video, "-an", "-sn",
            "-vf", string.Create(CultureInfo.InvariantCulture,
                $"fps={ReferenceLimits.FrameFps:0.###},scale={MotionCheckLimits.FrameWidth}:{MotionCheckLimits.FrameHeight},format=gray"),
            "-f", "rawvideo", output];

    /// <summary>The ffmpeg arguments that write the colour frames of <paramref name="video"/> into <paramref name="output"/> (pure).</summary>
    public static IReadOnlyList<string> ColorArguments(string video, string output) =>
        ["-hide_banner", "-loglevel", "error", "-y", "-t", ReferenceLimits.MaxSeconds.ToString(CultureInfo.InvariantCulture), "-i", video, "-an", "-sn",
            "-vf", string.Create(CultureInfo.InvariantCulture,
                $"fps={ReferenceLimits.ColorFps:0.###},scale={ReferenceLimits.ColorWidth}:{ReferenceLimits.ColorHeight},format=rgb24"),
            "-f", "rawvideo", output];

    /// <inheritdoc />
    public async Task<ReferenceFrameSeries?> ReadFramesAsync(string video, string work, CancellationToken cancellationToken)
    {
        var reader = new ReferenceFrameReader(ReferenceLimits.FrameFps);
        var read = await ReadAsync(video, work, ".gray", FrameArguments, ReferenceFrameReader.FrameBytes, (s, t) => reader.AddAllAsync(s, t), cancellationToken)
            .ConfigureAwait(false);
        return read ? reader.Series() : null;
    }

    /// <inheritdoc />
    public async Task<ReferenceColors?> ReadColorsAsync(string video, string work, CancellationToken cancellationToken)
    {
        var reader = new ReferenceColorReader();
        var read = await ReadAsync(video, work, ".rgb", ColorArguments, ReferenceColorReader.FrameBytes, (s, t) => reader.AddAllAsync(s, t), cancellationToken)
            .ConfigureAwait(false);
        return read ? reader.Result() : null;
    }

    private async Task<bool> ReadAsync(string video, string work, string extension, Func<string, string, IReadOnlyList<string>> arguments, int frameBytes,
        Func<Stream, CancellationToken, Task> add, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        Directory.CreateDirectory(work);
        var raw = Path.Combine(work, ".reference-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            var result = await runner.RunAsync(new ProcessStart(ffmpeg, [.. arguments(video, raw)], work), null, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || !File.Exists(raw))
            {
                logger.LogWarning("ffmpeg could not read the reference video (exit {Code}): {Error}", result.ExitCode,
                    ChildOutput.Summary(result.LastLines.Replace(work, "<folder>", StringComparison.Ordinal)
                        .Replace(Path.GetDirectoryName(Path.GetFullPath(video))!, "<folder>", StringComparison.Ordinal)).Trim());
                return false;
            }

            var stream = new FileStream(raw, FileMode.Open, FileAccess.Read, FileShare.Read, frameBytes * 4, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
            {
                await add(stream, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            try
            {
                File.Delete(raw);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A cancel kills ffmpeg, which can hold the file a moment longer: the measure's folder clean-up removes it.
                logger.LogInformation("The reference measure's frame file could not be removed yet ({Error})", ex.Message);
            }
        }
    }
}
