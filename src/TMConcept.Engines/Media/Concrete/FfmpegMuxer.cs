using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Media;

/// <summary>
/// Joins the silent video with the scene narrations (each placed at its scene start), burns the ASS file in (libass) when on-screen subtitles
/// are chosen or the template draws something (progress bar, closing card; with the template's fonts), lays the round channel photo over the
/// closing card, and fades through black at the start of every scene with a soft transition: the picture (subtitles too) goes dark over
/// <see cref="Core.Scenes.SceneTransitions.FadeHalfMs"/> before the scene's start and comes back over the same time after it, whatever the
/// video code drew; the timing does not change. Background music (0.1.44), when the template has it, is one more input of the narration's
/// mix with its own level envelope (<see cref="MusicChain"/>). Without narration, music, subtitles and fades the video is only copied. The
/// app's own ffmpeg; processor only.
/// </summary>
public sealed class FfmpegMuxer(ToolInstaller tools, IProcessRunner runner, ILogger<FfmpegMuxer> logger)
{
    /// <summary>
    /// Builds and runs ffmpeg. The files of <paramref name="overlay"/> are inside <paramref name="folder"/>; <paramref name="fadesMs"/> are the
    /// starts of the scenes that begin with a fade.
    /// </summary>
    public async Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, Core.Media.MuxOverlay overlay,
        IReadOnlyList<long> fadesMs, string output, CancellationToken cancellationToken)
    {
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        var arguments = Arguments(silentVideo, clips, overlay, fadesMs, output);
        var result = await runner.RunAsync(new ProcessStart(ffmpeg, arguments, folder), null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(output))
        {
            logger.LogError("ffmpeg mux failed ({Code}): {Lines}", result.ExitCode, EngineText(result.LastLines));
            throw new InvalidOperationException($"ffmpeg failed ({result.ExitCode}): {EngineText(result.LastLines)}");
        }
    }

    /// <summary>The ffmpeg arguments with only an ASS file (or none) over the video (pure; tests read them).</summary>
    public static IReadOnlyList<string> Arguments(string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, string? assFile,
        IReadOnlyList<long> fadesMs, string output) => Arguments(silentVideo, clips, new Core.Media.MuxOverlay(assFile), fadesMs, output);

    /// <summary>
    /// The ffmpeg arguments (pure; tests read them). The ASS file is burned in first (libass, with <see cref="Core.Media.MuxOverlay.FontsFolder"/>
    /// as its font folder), then the photo is cut to a circle and laid over it from its start (it is an input of its own, looped only for its
    /// own seconds and shifted to its start, so the circle is computed only for those frames), then the fades darken everything.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, Core.Media.MuxOverlay overlay,
        IReadOnlyList<long> fadesMs, string output)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(overlay);
        var c = CultureInfo.InvariantCulture;
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", silentVideo };
        foreach (var clip in clips)
        {
            arguments.AddRange(["-i", clip.Wav]);
        }

        var photo = overlay.Photo;
        if (photo is not null)
        {
            var seconds = Math.Max(0.1, photo.EndSeconds - photo.StartSeconds);
            arguments.AddRange(["-loop", "1", "-framerate", photo.Fps.ToString(c), "-t", seconds.ToString("0.###", c), "-i", photo.File]);
        }

        var music = overlay.Music;
        if (music is not null)
        {
            // A track shorter than the video plays again from its start (an input loop, no length to know in samples); it is cut at the
            // video's end below either way.
            if (music.Loop)
            {
                arguments.AddRange(["-stream_loop", "-1"]);
            }

            arguments.AddRange(["-i", music.File]);
        }

        var filter = new StringBuilder();
        var burn = overlay.AssFile is null
            ? null
            : "ass=" + overlay.AssFile + (overlay.FontsFolder is null ? string.Empty : ":fontsdir=" + overlay.FontsFolder);
        var fades = Fades(fadesMs);
        var drawn = burn is not null || photo is not null || fades.Count > 0;
        if (photo is null)
        {
            var video = new List<string>();
            if (burn is not null)
            {
                video.Add(burn);
            }

            video.AddRange(fades);
            if (video.Count > 0)
            {
                filter.Append("[0:v]").Append(string.Join(',', video)).Append("[v]");
            }
        }
        else
        {
            var index = clips.Count + 1;
            var size = photo.Size.ToString(c);
            var start = photo.StartSeconds.ToString("0.###", c);
            filter.Append(c, $"[{index}:v]scale={size}:{size}:force_original_aspect_ratio=increase,crop={size}:{size},format=rgba,")
                .Append("geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='if(lte(hypot(X-W/2,Y-H/2),W/2),255,0)',")
                .Append(c, $"fade=t=in:st=0:d={photo.FadeSeconds.ToString("0.###", c)}:alpha=1,setpts=PTS+{start}/TB[photo];");
            filter.Append(burn is null ? "[0:v]" : "[0:v]" + burn + "[base];[base]");
            filter.Append(c, $"[photo]overlay=x={photo.X}:y={photo.Y}:eof_action=pass:enable='gte(t,{start})'");
            if (fades.Count > 0)
            {
                filter.Append(',').Append(string.Join(',', fades));
            }

            filter.Append("[v]");
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
        }

        if (music is not null)
        {
            if (filter.Length > 0)
            {
                filter.Append(';');
            }

            filter.Append(c, $"[{clips.Count + 1 + (photo is null ? 0 : 1)}:a]").Append(MusicChain(music)).Append(clips.Count > 0 ? "[music]" : "[a]");
        }

        if (clips.Count > 0)
        {
            filter.Append(';');
            for (var i = 0; i < clips.Count; i++)
            {
                filter.Append(c, $"[a{i}]");
            }

            // The music is one more input of the same mix (no normalising: the narration keeps its level, the music its envelope).
            filter.Append(music is null ? string.Empty : "[music]")
                .Append(c, $"amix=inputs={clips.Count + (music is null ? 0 : 1)}:normalize=0:dropout_transition=0[a]");
        }

        if (filter.Length > 0)
        {
            arguments.AddRange(["-filter_complex", filter.ToString()]);
        }

        arguments.AddRange(["-map", drawn ? "[v]" : "0:v"]);
        if (clips.Count > 0 || music is not null)
        {
            arguments.AddRange(["-map", "[a]", "-c:a", "aac", "-b:a", "192k"]);
        }

        arguments.AddRange(drawn ? ["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p"] : ["-c:v", "copy"]);
        arguments.AddRange(["-movflags", "+faststart", output]);
        return arguments;
    }

    /// <summary>
    /// The music's filters (0.1.44, C3): 48 kHz stereo, cut at the video's length (a looped track too), the level envelope evaluated every
    /// frame (<see cref="Core.Media.MusicEnvelope.Expression"/>: ducked under the narration) and the fade-out at the end.
    /// </summary>
    public static string MusicChain(Core.Media.MuxMusic music)
    {
        ArgumentNullException.ThrowIfNull(music);
        var c = CultureInfo.InvariantCulture;
        var length = Math.Max(0.1, music.DurationSeconds);
        if (music.StartSeconds > 0 || music.EndSeconds is not null || music.FadeInSeconds > 0)
        {
            return WindowChain(music, length);
        }

        var chain = new StringBuilder(string.Create(c, $"aresample=48000,aformat=channel_layouts=stereo,atrim=duration={length:0.###},asetpts=PTS-STARTPTS"));
        chain.Append(",volume='").Append(music.Volume).Append("':eval=frame");
        var fade = Math.Min(Math.Max(0, music.FadeOutSeconds), length);
        if (fade > 0)
        {
            chain.Append(c, $",afade=t=out:st={length - fade:0.###}:d={fade:0.###}");
        }

        return chain.ToString();
    }

    /// <summary>
    /// The music's filters when it plays under only some scenes or fades in (0.1.45, the scene plan's hints): cut to the part it plays, faded in
    /// at its start and out before its end (the video's end takes the template's fade-out), delayed to its start, then the level envelope
    /// (its <c>t</c> is the video's time after the delay).
    /// </summary>
    private static string WindowChain(Core.Media.MuxMusic music, double length)
    {
        var c = CultureInfo.InvariantCulture;
        var start = Math.Clamp(music.StartSeconds, 0, length - 0.1);
        var end = Math.Clamp(music.EndSeconds ?? length, start + 0.1, length);
        var playing = end - start;
        var chain = new StringBuilder(string.Create(c, $"aresample=48000,aformat=channel_layouts=stereo,atrim=duration={playing:0.###},asetpts=PTS-STARTPTS"));
        var fadeIn = Math.Min(Math.Max(0, music.FadeInSeconds), playing / 2);
        if (fadeIn > 0)
        {
            chain.Append(c, $",afade=t=in:st=0:d={fadeIn:0.###}");
        }

        var fadeOut = Math.Min(Math.Max(0, music.FadeOutSeconds), playing - fadeIn);
        if (fadeOut > 0)
        {
            chain.Append(c, $",afade=t=out:st={playing - fadeOut:0.###}:d={fadeOut:0.###}");
        }

        if (start > 0)
        {
            var delay = Math.Round(start * 1000).ToString("0", c);
            chain.Append(c, $",adelay=delays={delay}:all=1");
        }

        chain.Append(",volume='").Append(music.Volume).Append("':eval=frame");
        return chain.ToString();
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
