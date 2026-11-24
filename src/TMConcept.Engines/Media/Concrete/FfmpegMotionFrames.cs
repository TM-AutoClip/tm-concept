using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Media;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Core.Videos;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Media;

/// <summary>
/// ffmpeg behind <see cref="IMotionFrames"/> (0.1.41): the app's own ffmpeg (the components' copy, not the one on PATH) writes every frame
/// as <see cref="MotionCheckLimits.FrameWidth"/> × <see cref="MotionCheckLimits.FrameHeight"/> grey bytes into a temporary file, which is read
/// one frame at a time (<see cref="MotionFrameReader"/>) and deleted; a strip is ffmpeg's tile filter into a PNG. The process runner starts
/// ffmpeg at below-normal priority.
/// </summary>
public sealed class FfmpegMotionFrames(ToolInstaller tools, IProcessRunner runner, ILogger<FfmpegMotionFrames> logger) : IMotionFrames
{
    /// <summary>The ffmpeg arguments that write every frame of <paramref name="video"/> at <paramref name="fps"/> as raw grey bytes.</summary>
    public static IReadOnlyList<string> FrameArguments(string video, double fps, string output) =>
        ["-hide_banner", "-loglevel", "error", "-y", "-i", video, "-an", "-vf",
            string.Create(CultureInfo.InvariantCulture, $"fps={fps:0.###},scale={MotionCheckLimits.FrameWidth}:{MotionCheckLimits.FrameHeight},format=gray"),
            "-f", "rawvideo", output];

    /// <summary>
    /// The ffmpeg arguments of a strip: <see cref="MotionCheckLimits.StripFrames"/> frames from around <paramref name="atSeconds"/> (consecutive,
    /// or spread over <paramref name="spanSeconds"/>), side by side in one PNG.
    /// </summary>
    public static IReadOnlyList<string> StripArguments(string video, double atSeconds, double spanSeconds, double fps, string png)
    {
        var c = CultureInfo.InvariantCulture;
        const int frames = MotionCheckLimits.StripFrames;
        var spread = spanSeconds > 0;
        var start = Math.Max(0, spread ? atSeconds - (spanSeconds / 2) : atSeconds - (frames / 2 / Math.Max(1, fps)));
        var filter = (spread ? string.Create(c, $"fps={frames / spanSeconds:0.###},") : string.Empty)
            + string.Create(c, $"scale={MotionCheckLimits.StripFrameWidth}:-2,tile={frames}x1");
        return ["-hide_banner", "-loglevel", "error", "-y", "-ss", start.ToString("0.###", c), "-i", video, "-an", "-vf", filter, "-frames:v", "1",
            "-update", "1", png];
    }

    /// <inheritdoc />
    public async Task<MotionSeries?> ReadAsync(string video, double fps, string workFolder, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        Directory.CreateDirectory(workFolder);
        var raw = Path.Combine(workFolder, ".motion-" + Guid.NewGuid().ToString("N") + ".gray");
        try
        {
            var result = await runner.RunAsync(new ProcessStart(ffmpeg, [.. FrameArguments(video, fps, raw)], workFolder), null, cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0 || !File.Exists(raw))
            {
                logger.LogWarning("ffmpeg could not read the frames of {File} for the motion check (exit {Code}): {Error}", Path.GetFileName(video),
                    result.ExitCode, ChildOutput.Summary(result.LastLines.Replace(workFolder, "<folder>", StringComparison.Ordinal)).Trim());
                return null;
            }

            var reader = new MotionFrameReader(fps);
            var stream = new FileStream(raw, FileMode.Open, FileAccess.Read, FileShare.Read, MotionFrameReader.FrameBytes * 4, FileOptions.Asynchronous
                | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
            {
                await reader.AddAllAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            return reader.Series();
        }
        finally
        {
            if (File.Exists(raw))
            {
                File.Delete(raw);
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> StripAsync(string video, double atSeconds, double spanSeconds, double fps, string png, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        var folder = Path.GetDirectoryName(Path.GetFullPath(png))!;
        Directory.CreateDirectory(folder);
        var result = await runner.RunAsync(new ProcessStart(ffmpeg, [.. StripArguments(video, atSeconds, spanSeconds, fps, png)], folder), null,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0 && File.Exists(png))
        {
            return true;
        }

        logger.LogWarning("ffmpeg could not write the frame strip {File} (exit {Code}): {Error}", Path.GetFileName(png), result.ExitCode,
            ChildOutput.Summary(result.LastLines.Replace(folder, "<folder>", StringComparison.Ordinal)).Trim());
        return false;
    }
}
