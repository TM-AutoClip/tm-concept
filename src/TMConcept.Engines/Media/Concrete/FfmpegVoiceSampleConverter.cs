using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Media;

/// <summary>
/// <see cref="IVoiceSampleConverter"/> with ffmpeg (a separate process at low priority, like every child process): the first audio stream
/// of any file ffmpeg reads, mixed to mono, resampled to 24 kHz, 16-bit PCM, cut to the given length. The log line of a failure never
/// names the user's file (only ffmpeg's last lines with the paths replaced).
/// </summary>
public sealed class FfmpegVoiceSampleConverter(ToolInstaller tools, IProcessRunner runner, ILogger<FfmpegVoiceSampleConverter> logger) : IVoiceSampleConverter
{
    /// <inheritdoc />
    public IReadOnlyList<string> RequiredComponents { get; } = [Core.Components.ComponentCodes.Ffmpeg];

    /// <inheritdoc />
    public async Task<bool> ConvertAsync(string input, string outputWav, int maxSeconds, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        var folder = Path.GetDirectoryName(Path.GetFullPath(outputWav))!;
        Directory.CreateDirectory(folder);
        var result = await runner.RunAsync(new ProcessStart(ffmpeg,
        [
            "-hide_banner", "-loglevel", "error", "-y", "-i", input, "-map", "0:a:0", "-vn", "-ac", "1",
            "-ar", VoiceSampleLimits.SampleRate.ToString(CultureInfo.InvariantCulture), "-sample_fmt", "s16", "-c:a", "pcm_s16le",
            "-t", maxSeconds.ToString(CultureInfo.InvariantCulture), outputWav,
        ], folder), null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0 && File.Exists(outputWav) && new FileInfo(outputWav).Length > 44)
        {
            return true;
        }

        var lines = result.LastLines.Replace(input, "<recording>", StringComparison.Ordinal).Replace(outputWav, "<sample>", StringComparison.Ordinal);
        logger.LogWarning("ffmpeg could not read the voice recording (exit {Code}): {Error}", result.ExitCode, ChildOutput.Summary(lines).Trim());
        return false;
    }
}
