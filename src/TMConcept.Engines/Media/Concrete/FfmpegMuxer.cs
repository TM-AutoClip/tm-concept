using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Media;

/// <summary>
/// Joins the silent video with the scene narrations (each placed at its scene start), burns the ASS file in (libass) when on-screen subtitles
/// are chosen, and fades through black at the start of every scene with a soft transition: the picture (subtitles too) goes dark over
/// <see cref="Core.Scenes.SceneTransitions.FadeHalfMs"/> before the scene's start and comes back over the same time after it, whatever the
/// video code drew; the timing does not change. Without narration, subtitles and fades the video is only copied. The app's own ffmpeg;
/// processor only.
/// </summary>
public sealed class FfmpegMuxer(ToolInstaller tools, IProcessRunner runner, ILogger<FfmpegMuxer> logger)
{
    /// <summary>
    /// Builds and runs ffmpeg. <paramref name="assFile"/> is a file name inside <paramref name="folder"/> (or null); <paramref name="fadesMs"/>
    /// are the starts of the scenes that begin with a fade.
    /// </summary>
    public async Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, string? assFile,
        IReadOnlyList<long> fadesMs, string output, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        var arguments = Arguments(silentVideo, clips, assFile, fadesMs, output);
        var result = await runner.RunAsync(new ProcessStart(ffmpeg, arguments, folder), null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(output))
        {
            logger.LogError("ffmpeg mux failed ({Code}): {Lines}", result.ExitCode, EngineText(result.LastLines));
            throw new InvalidOperationException($"ffmpeg failed ({result.ExitCode}): {EngineText(result.LastLines)}");
        }
    }

    /// <summary>The ffmpeg arguments (pure; tests read them).</summary>
    public static IReadOnlyList<string> Arguments(string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, string? assFile,
        IReadOnlyList<long> fadesMs, string output)
    {
        ArgumentNullException.ThrowIfNull(clips);
        var c = CultureInfo.InvariantCulture;
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", silentVideo };
        foreach (var clip in clips)
        {
            arguments.AddRange(["-i", clip.Wav]);
        }

        var filter = new StringBuilder();
        var video = new List<string>();
        if (assFile is not null)
        {
            video.Add("ass=" + assFile);
        }

        video.AddRange(Fades(fadesMs));
        if (video.Count > 0)
        {
            filter.Append("[0:v]").Append(string.Join(',', video)).Append("[v]");
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

        arguments.AddRange(["-map", video.Count == 0 ? "0:v" : "[v]"]);
        if (clips.Count > 0)
        {
            arguments.AddRange(["-map", "[a]", "-c:a", "aac", "-b:a", "192k"]);
        }

        arguments.AddRange(video.Count == 0 ? ["-c:v", "copy"] : ["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p"]);
        arguments.AddRange(["-movflags", "+faststart", output]);
        return arguments;
    }

    /// <summary>
    /// Two fades per soft transition, each only inside its own part (<c>enable</c>): out to black just before the scene's start, in from black
    /// just after it; outside them the picture passes unchanged (a plain fade out would leave everything after it black). Times in invariant
    /// seconds; a transition at the very start is skipped.
    /// </summary>
    public static IReadOnlyList<string> Fades(IReadOnlyList<long> fadesMs)
    {
        ArgumentNullException.ThrowIfNull(fadesMs);
        var c = CultureInfo.InvariantCulture;
        var half = Core.Scenes.SceneTransitions.FadeHalfMs / 1000.0;
        var filters = new List<string>();
        foreach (var at in fadesMs.Where(f => f > 0).Distinct().Order())
        {
            var start = at / 1000.0;
            var before = Math.Max(0, start - half);
            filters.Add(string.Create(c, $"fade=t=out:st={before:0.###}:d={start - before:0.###}:enable='between(t,{before:0.###},{start:0.###})'"));
            filters.Add(string.Create(c, $"fade=t=in:st={start:0.###}:d={half:0.###}:enable='between(t,{start:0.###},{start + half:0.###})'"));
        }

        return filters;
    }

    private static string EngineText(string text) => Core.Processes.ChildOutput.Summary(text);
}
