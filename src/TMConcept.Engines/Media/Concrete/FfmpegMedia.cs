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
    public Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, string? assFile, string output,
        CancellationToken cancellationToken) => muxer.MuxAsync(folder, silentVideo, clips, assFile, output, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ThumbnailAsync(string video, string jpeg, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        var folder = Path.GetDirectoryName(jpeg)!;
        var probe = Path.Combine(folder, ".probe.gray");
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
                    continue;
                }

                var pixels = await File.ReadAllBytesAsync(probe, cancellationToken).ConfigureAwait(false);
                if (pixels.Max() < 40 && pixels.Average(p => p) < 6)
                {
                    continue;
                }

                var shot = await runner.RunAsync(new ProcessStart(ffmpeg,
                    ["-hide_banner", "-loglevel", "error", "-y", "-ss", at, "-i", video, "-frames:v", "1", "-vf", "scale=480:-2", "-q:v", "3", jpeg], folder),
                    null, cancellationToken).ConfigureAwait(false);
                return shot.ExitCode == 0 && File.Exists(jpeg);
            }

            logger.LogWarning("No frame that is not black was found for the thumbnail");
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
