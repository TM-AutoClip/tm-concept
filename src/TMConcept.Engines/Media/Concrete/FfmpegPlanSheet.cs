using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Media;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Core.Videos;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Media;

/// <summary>
/// ffmpeg behind <see cref="IPlanSheet"/> (F10, 0.1.49): the app's own ffmpeg opens the preview video once per moment (seeking before the
/// input), takes its first frame there, scales it to <see cref="PlanPreviewLimits.FrameWidth"/>, joins them and lays them out with the tile
/// filter (a last row that is not full is written as it is). The process runner starts ffmpeg at below-normal priority.
/// </summary>
public sealed class FfmpegPlanSheet(ToolInstaller tools, IProcessRunner runner, ILogger<FfmpegPlanSheet> logger) : IPlanSheet
{
    /// <summary>The ffmpeg arguments of a sheet.</summary>
    public static IReadOnlyList<string> Arguments(string video, IReadOnlyList<double> seconds, int columns, string png)
    {
        ArgumentNullException.ThrowIfNull(seconds);
        var c = CultureInfo.InvariantCulture;
        var count = seconds.Count;
        columns = Math.Clamp(columns, 1, Math.Max(1, count));
        var rows = (count + columns - 1) / columns;
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        foreach (var at in seconds)
        {
            arguments.AddRange(["-ss", Math.Max(0, at).ToString("0.###", c), "-i", video]);
        }

        var filter = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            filter.Append(c, $"[{i}:v]trim=end_frame=1,setpts=PTS-STARTPTS,scale={PlanPreviewLimits.FrameWidth}:-2,setsar=1[f{i}];");
        }

        for (var i = 0; i < count; i++)
        {
            filter.Append(c, $"[f{i}]");
        }

        filter.Append(c, $"concat=n={count}:v=1:a=0,tile={columns}x{rows}:padding=4:color=black[sheet]");
        arguments.AddRange(["-filter_complex", filter.ToString(), "-map", "[sheet]", "-frames:v", "1", "-update", "1", png]);
        return arguments;
    }

    /// <inheritdoc />
    public async Task<bool> SheetAsync(string video, IReadOnlyList<double> seconds, int columns, string png, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seconds);
        if (seconds.Count == 0)
        {
            return false;
        }

        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        var folder = Path.GetDirectoryName(Path.GetFullPath(png))!;
        Directory.CreateDirectory(folder);
        var result = await runner.RunAsync(new ProcessStart(ffmpeg, [.. Arguments(video, seconds, columns, png)], folder), null, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode == 0 && File.Exists(png))
        {
            return true;
        }

        logger.LogWarning("ffmpeg could not write the plan sheet {File} (exit {Code}): {Error}", Path.GetFileName(png), result.ExitCode,
            ChildOutput.Summary(result.LastLines.Replace(folder, "<folder>", StringComparison.Ordinal)).Trim());
        return false;
    }
}
