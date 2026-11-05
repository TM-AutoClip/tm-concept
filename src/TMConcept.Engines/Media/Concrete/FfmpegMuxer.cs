using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Media;

/// <summary>
/// Joins the silent video with the scene narrations (each placed at its scene start) and, when on-screen subtitles are chosen, burns the
/// ASS file in (libass). Without narration and subtitles the video is only copied. The app's own ffmpeg; processor only.
/// </summary>
public sealed class FfmpegMuxer(ToolInstaller tools, IProcessRunner runner, ILogger<FfmpegMuxer> logger)
{
    /// <summary>Builds and runs ffmpeg. <paramref name="assFile"/> is a file name inside <paramref name="folder"/> (or null).</summary>
    public async Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, string? assFile, string output,
        CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        var arguments = Arguments(silentVideo, clips, assFile, output);
        var result = await runner.RunAsync(new ProcessStart(ffmpeg, arguments, folder), null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(output))
        {
            logger.LogError("ffmpeg mux failed ({Code}): {Lines}", result.ExitCode, EngineText(result.LastLines));
            throw new InvalidOperationException($"ffmpeg failed ({result.ExitCode}): {EngineText(result.LastLines)}");
        }
    }

    /// <summary>The ffmpeg arguments (pure; tests read them).</summary>
    public static IReadOnlyList<string> Arguments(string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, string? assFile, string output)
    {
        ArgumentNullException.ThrowIfNull(clips);
        var c = CultureInfo.InvariantCulture;
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", silentVideo };
        foreach (var clip in clips)
        {
            arguments.AddRange(["-i", clip.Wav]);
        }

        var filter = new StringBuilder();
        if (assFile is not null)
        {
            filter.Append("[0:v]ass=").Append(assFile).Append("[v]");
        }

        if (clips.Count > 0)
        {
            for (var i = 0; i < clips.Count; i++)
            {
                if (filter.Length > 0)
                {
                    filter.Append(';');
                }

                var delay = Math.Max(0, clips[i].StartMs).ToString(c);
                filter.Append(c, $"[{i + 1}:a]aresample=48000,adelay={delay}|{delay}[a{i}]");
            }

            filter.Append(';');
            for (var i = 0; i < clips.Count; i++)
            {
                filter.Append(c, $"[a{i}]");
            }

            filter.Append(c, $"amix=inputs={clips.Count}:normalize=0:dropout_transition=0[a]");
        }

        if (filter.Length > 0)
        {
            arguments.AddRange(["-filter_complex", filter.ToString()]);
        }

        arguments.AddRange(["-map", assFile is null ? "0:v" : "[v]"]);
        if (clips.Count > 0)
        {
            arguments.AddRange(["-map", "[a]", "-c:a", "aac", "-b:a", "192k"]);
        }

        arguments.AddRange(assFile is null ? ["-c:v", "copy"] : ["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p"]);
        arguments.AddRange(["-movflags", "+faststart", output]);
        return arguments;
    }

    private static string EngineText(string text) => Engines.EnginePackage.Short(text);
}
