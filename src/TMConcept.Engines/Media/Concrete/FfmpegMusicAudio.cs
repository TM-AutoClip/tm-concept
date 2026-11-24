using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Media;

/// <summary>
/// The music analysis' sound work behind <see cref="IMusicAudio"/> (0.1.45, F7 + F8), with the app's ffmpeg: a track read as mono 32-bit
/// float samples into a raw file (no Python, no outside library), and the laid-out track (entry point, loop copies joined by cross-fades)
/// written as FLAC.
/// </summary>
public sealed class FfmpegMusicAudio(ToolInstaller tools, IProcessRunner runner, ILogger<FfmpegMusicAudio> logger) : IMusicAudio
{
    /// <summary>The ffmpeg arguments that read <paramref name="file"/> as mono float samples into <paramref name="output"/> (pure).</summary>
    public static IReadOnlyList<string> ReadArguments(string file, int sampleRate, int maxSeconds, string output) =>
        ["-hide_banner", "-loglevel", "error", "-y", "-t", maxSeconds.ToString(CultureInfo.InvariantCulture), "-i", file, "-vn", "-ac", "1", "-ar",
            sampleRate.ToString(CultureInfo.InvariantCulture), "-f", "f32le", output];

    /// <summary>
    /// The ffmpeg arguments that write <paramref name="bed"/> of <paramref name="file"/> to <paramref name="output"/> (pure; tests read them):
    /// the part from the entry to the loop's end at 48 kHz stereo, split into the copies, each joined to the next with <c>acrossfade</c>, cut
    /// at the needed length.
    /// </summary>
    public static IReadOnlyList<string> BedArguments(string file, MusicBed bed, string output)
    {
        ArgumentNullException.ThrowIfNull(bed);
        var c = CultureInfo.InvariantCulture;
        var filter = new StringBuilder(string.Create(c,
            $"[0:a]aresample=48000,aformat=channel_layouts=stereo,atrim=start={bed.EntrySeconds:0.###}:end={bed.LoopEndSeconds:0.###},asetpts=PTS-STARTPTS"));
        if (bed.Copies <= 1)
        {
            filter.Append("[bed]");
        }
        else
        {
            filter.Append(c, $",asplit={bed.Copies}");
            for (var i = 0; i < bed.Copies; i++)
            {
                filter.Append(c, $"[c{i}]");
            }

            var previous = "c0";
            for (var i = 1; i < bed.Copies; i++)
            {
                var next = i == bed.Copies - 1 ? "bed" : "x" + i.ToString(c);
                filter.Append(c, $";[{previous}][c{i}]acrossfade=d={bed.CrossfadeSeconds:0.###}:c1=tri:c2=tri[{next}]");
                previous = next;
            }
        }

        filter.Append(c, $";[bed]atrim=duration={Math.Min(bed.TotalSeconds, bed.LengthSeconds + 0.5):0.###}[out]");
        return ["-hide_banner", "-loglevel", "error", "-y", "-i", file, "-filter_complex", filter.ToString(), "-map", "[out]", "-c:a", "flac", output];
    }

    /// <inheritdoc />
    public async Task<float[]?> ReadMonoAsync(string file, int sampleRate, int maxSeconds, string work, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        Directory.CreateDirectory(work);
        var raw = Path.Combine(work, "analysis.f32");
        try
        {
            var result = await runner.RunAsync(new ProcessStart(ffmpeg, ReadArguments(file, sampleRate, maxSeconds, raw), work), null, cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0 || !File.Exists(raw))
            {
                logger.LogWarning("ffmpeg could not read {File} for its beats ({Code})", Path.GetFileName(file), result.ExitCode);
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(raw, cancellationToken).ConfigureAwait(false);
            return MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, bytes.Length - (bytes.Length % sizeof(float)))).ToArray();
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
    public async Task<bool> RenderBedAsync(string file, MusicBed bed, string output, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var result = await runner.RunAsync(new ProcessStart(ffmpeg, BedArguments(file, bed, output), Path.GetDirectoryName(output)!), null, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(output))
        {
            logger.LogWarning("ffmpeg could not lay out the music ({Code}): {Lines}", result.ExitCode, ChildOutput.Summary(result.LastLines
                .Replace(Path.GetDirectoryName(output)!, "<work>", StringComparison.Ordinal)
                .Replace(Path.GetDirectoryName(Path.GetFullPath(file))!, "<folder>", StringComparison.Ordinal)).Trim());
            return false;
        }

        return true;
    }
}
