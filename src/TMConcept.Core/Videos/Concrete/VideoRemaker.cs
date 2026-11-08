using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Engines;
using TMConcept.Core.Markers;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Videos;

/// <summary>
/// New versions of a made video (KURALLAR 12): every one is a new version folder next to the old ones (v2/, v3/…) with its own record;
/// older versions are never changed.
/// <list type="bullet">
/// <item><see cref="RevoiceAsync"/>: the narration text edited per scene (placeholders: pauses become silence, speed markers change the
/// voice's speed, a transition placeholder sets how the scene begins) and a voice chosen; the voice is made again on this computer (an online
/// voice is paid, nothing else; <see cref="SceneNarrator"/>, the same as the first video), each scene lasts as long as its new clip (a silent
/// one as long as its text takes to say, pauses included), <c>input.json</c> gets the new timing, texts and transitions, and the SAME kept
/// code is rendered again — no model call. The subtitles follow the new text; a fade is added by ffmpeg at the mux.</item>
/// <item><see cref="RecodeAsync"/>: another engine (HyperFrames ↔ Remotion ↔ Revideo) needs its own code: the code model writes it for the
/// same input (paid, with the usual one repair), and the kept scene images, clips and texts are used again.</item>
/// </list>
/// </summary>
public sealed class VideoRemaker(IEnumerable<IVideoEngine> engines, SceneNarrator narrator, VideoAssembler assembler, IAppPaths paths,
    ILogger<VideoRemaker> logger)
{
    private static readonly (string Step, double Weight)[] RevoiceWeights = [("prepare", 2), ("speech", 30), ("timing", 1), ("render", 55), ("mux", 10), ("save", 2)];

    private static readonly (string Step, double Weight)[] RecodeWeights = [("prepare", 2), ("code", 25), ("render", 60), ("mux", 10), ("save", 3)];

    /// <summary>
    /// What a new version starts from: the version's folder, its engine (from its settings), the kept code and <c>input.json</c>. A version
    /// without them (made before 0.1.7 kept them, or files removed from outside) cannot be made again: <see cref="VideoException"/> "source".
    /// </summary>
    public VersionSource Load(VideoRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var folder = LibraryPaths.Resolve(paths.LibraryDirectory, record.Folder);
        var options = Options(record);
        var engine = engines.FirstOrDefault(e => e.Kind == options.Engine)
            ?? throw new VideoException($"Engine {options.Engine} is not available.") { Code = "source" };
        var project = Path.Combine(folder, VideoFolders.Source);
        var inputFile = Path.Combine(project, VideoFolders.InputFile);
        var codeFile = Path.Combine(project, engine.CodeFile);
        if (!File.Exists(inputFile) || !File.Exists(codeFile))
        {
            throw new VideoException("The version has no kept project (source/input.json and the code file).") { Code = "source" };
        }

        var input = RenderInputBuilder.FromJson(File.ReadAllText(inputFile))
            ?? throw new VideoException("The kept input.json of the version cannot be read.") { Code = "source" };
        return new VersionSource(record, folder, engine, File.ReadAllText(codeFile), input, Texts(folder, input));
    }

    /// <summary>A new version with the edited narration and the chosen voice, rendered on this computer with the kept code (no model call).</summary>
    public async Task<VideoRecord> RevoiceAsync(RevoiceJob job, IProgress<VideoProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var source = job.Source;
        var input = source.Input;
        if (job.SceneTexts.Count != input.Scenes.Count)
        {
            throw new ArgumentException("One narration text per scene is needed.", nameof(job));
        }

        Ready(source.Engine);
        var id = Guid.NewGuid();
        var watch = Stopwatch.StartNew();
        var work = assembler.WorkFolder(id);
        var target = new VersionTarget();
        var spent = new List<VideoCost>();
        var voice = job.Voice;
        var steps = new VideoStepProgress(RevoiceWeights.Where(w => w.Step != "speech" || voice is not null).ToList(), progress);
        logger.LogInformation("New version {Id} with a new voice started: {Engine}, voice {Voice}", id, source.Engine.Name,
            voice is null ? "off" : voice.Model.Provider + "/" + voice.Model.Id);
        return await assembler.GuardAsync(job.Profile.Id, voice?.Online, work, target, spent, watch, async () =>
        {
            steps.Report("prepare", 0);
            VideoAssembler.CopyProject(Path.Combine(source.Folder, VideoFolders.Source), Path.Combine(work, "project"));
            steps.Finish("prepare");

            var warnings = new List<string>();
            var clips = new List<SpeechClip?>();
            if (voice is null)
            {
                warnings.Add(VideoWarnings.SpeechOff);
                clips.AddRange(input.Scenes.Select(_ => (SpeechClip?)null));
            }
            else
            {
                var skipped = 0;
                for (var i = 0; i < input.Scenes.Count; i++)
                {
                    var (clip, words) = await SceneVoiceAsync(voice, job.Profile.ContentLanguage, i + 1, job.SceneTexts[i], work, spent, id, cancellationToken)
                        .ConfigureAwait(false);
                    skipped += words;
                    clips.Add(clip);
                    steps.Report("speech", (i + 1.0) / input.Scenes.Count);
                }

                warnings.Add(VideoWarnings.AiVoice);
                if (skipped > 0)
                {
                    warnings.Add(VideoWarnings.SpeechSkipped);
                    logger.LogWarning("The voice left out {Count} words it does not know; the subtitles still show them", skipped);
                }

                steps.Finish("speech");
            }

            // C. timing: each scene lasts as long as its new clip (pauses included); a silent scene as long as its text takes to say.
            var sources = input.Scenes.Select((s, i) =>
            {
                var script = job.SceneTexts[i];
                var spoken = MarkerText.Spoken(script);
                return new RenderSceneSource(s.Visual, spoken, s.Image, clips[i]?.DurationMs ?? SilentMs(script, input.Language), MarkerText.Transition(script));
            }).ToList();
            var timed = RenderInputBuilder.Build(input.Title, input.Language, sources, input.Width, input.Height, input.Fps, input.Theme);
            steps.Finish("timing");

            var rendered = await assembler.RenderKeptAsync(source.Engine, source.Code, timed, work, steps, cancellationToken).ConfigureAwait(false);
            var options = Options(source.Record);
            options.Speech = voice is not null;
            warnings.AddRange(ImageWarnings(source.Record));
            var record = await assembler.SaveAsync(new VersionSave(id, job.Profile, source.Record.ConceptId, source.Record.Title, work, rendered, clips, target,
                VersionFolders.Next(Path.GetDirectoryName(source.Folder)!), options, source.Engine.Name, source.Record.CodeModel,
                voice is null ? string.Empty : voice.Model.Provider + "/" + voice.Model.Id + ":" + voice.Voice, source.Record.ImageModel, source.Record.PromptVersion,
                warnings, spent, [], watch) { Script = job.SceneTexts }, steps, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("New version {Id} made in {Seconds:0} s with the kept code ({Usd} USD for the voice)", id, watch.Elapsed.TotalSeconds,
                record.TotalUsd);
            assembler.TryDelete(work);
            return record;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// A new version for another engine: new code from the code model for the same input (paid; one repair at most), the kept scene images
    /// and clips used again.
    /// </summary>
    public async Task<VideoRecord> RecodeAsync(RecodeJob job, IProgress<VideoProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var source = job.Source;
        var input = source.Input;
        Ready(job.Engine);
        var id = Guid.NewGuid();
        var watch = Stopwatch.StartNew();
        var work = assembler.WorkFolder(id);
        var target = new VersionTarget();
        var spent = new List<VideoCost>();
        var steps = new VideoStepProgress(RecodeWeights, progress);
        logger.LogInformation("New version {Id} for {Engine} started (code model {Provider}/{Model})", id, job.Engine.Name, job.CodeModel.Model.Provider,
            job.CodeModel.Model.Id);
        return await assembler.GuardAsync(job.Profile.Id, job.CodeModel, work, target, spent, watch, async () =>
        {
            steps.Report("prepare", 0);
            PlaceImages(source, Path.Combine(work, "project"));
            var clips = input.Scenes.Select(s => VersionFolders.Clip(source.Folder, s.Number))
                .Select(f => File.Exists(f) ? new SpeechClip(f, WaveFile.DurationMs(f), 0, 0, "kept") : null).ToList();
            steps.Finish("prepare");

            var options = Options(source.Record);
            var rendered = await assembler.CodeAndRenderAsync(job.Engine, job.CodeModel, options.Subtitles, input, work, spent, id, steps, cancellationToken)
                .ConfigureAwait(false);
            var warnings = source.Record.Warnings.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(w => w != VideoWarnings.Repaired).ToList();
            if (rendered.Repaired)
            {
                warnings.Add(VideoWarnings.Repaired);
            }

            options.Engine = job.Engine.Kind;
            options.CodeProvider = job.CodeModel.Model.Provider;
            options.CodeModel = job.CodeModel.Model.Id;
            var record = await assembler.SaveAsync(new VersionSave(id, job.Profile, source.Record.ConceptId, source.Record.Title, work, rendered, clips, target,
                VersionFolders.Next(Path.GetDirectoryName(source.Folder)!), options, job.Engine.Name, job.CodeModel.Model.Provider + "/" + job.CodeModel.Model.Id,
                source.Record.SpeechModel, source.Record.ImageModel, VideoCodePrompt.Version, warnings, spent, [], watch) { Script = source.Texts }, steps,
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation("New version {Id} for {Engine} made in {Seconds:0} s ({Usd} USD)", id, job.Engine.Name, watch.Elapsed.TotalSeconds, record.TotalUsd);
            assembler.TryDelete(work);
            return record;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// How long a scene without a voice lasts: as long as its text takes to say, with the silence of its pauses (the first video and every new
    /// version time it the same way).
    /// </summary>
    public static long SilentMs(string? script, string language) =>
        VideoCostEstimator.Milliseconds(MarkerText.Spoken(script), language) + MarkerText.Silence(script);

    /// <summary>
    /// A scene's text to edit as kept in a version: its placeholders and the transition into it. The texts kept with a version (placeholders
    /// included) when there are as many as scenes; otherwise the spoken captions, each after its scene's transition placeholder.
    /// </summary>
    private IReadOnlyList<string> Texts(string folder, RenderInput input)
    {
        var file = Path.Combine(folder, VideoFolders.ScriptFile);
        if (File.Exists(file))
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllText(file), CoreJsonContext.Default.ListString) is { } script && script.Count == input.Scenes.Count)
                {
                    return script;
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "The edited narration of a version could not be read; its spoken text is used");
            }
        }

        return input.Scenes.Select(s => WithTransition(s.Transition, s.Caption, input.Language)).ToList();
    }

    /// <summary>The settings a version was made with (a fresh copy each time).</summary>
    public static VideoOptions Options(VideoRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            return JsonSerializer.Deserialize(record.SettingsJson, CoreJsonContext.Default.VideoOptions) ?? new VideoOptions();
        }
        catch (JsonException)
        {
            return new VideoOptions();
        }
    }

    /// <summary>
    /// <paramref name="text"/> after the placeholder of <paramref name="transition"/> in <paramref name="language"/> (a scene's edit text starts
    /// with how the scene begins); the text alone when there is no transition.
    /// </summary>
    public static string WithTransition(string transition, string text, string language)
    {
        var marker = MarkerCatalog.Of(MarkerKind.Transition).FirstOrDefault(m => m.Transition == transition);
        return marker is null || transition.Length == 0 ? text : (marker.TokenOf(language) + " " + text).TrimEnd();
    }

    /// <summary>One scene's new clip (<see cref="SceneNarrator"/>), with what it cost in <paramref name="spent"/>; a voice that fails stops the version.</summary>
    private async Task<(SpeechClip? Clip, int Skipped)> SceneVoiceAsync(SpeechSetup voice, string language, int number, string text, string work,
        List<VideoCost> spent, Guid id, CancellationToken cancellationToken)
    {
        decimal cost = 0;
        try
        {
            var clip = await narrator.VoiceAsync(voice, language, number, text, work, usd => cost += usd, cancellationToken).ConfigureAwait(false);
            return (clip, clip?.SkippedWords.Count ?? 0);
        }
        catch (Exception ex) when (ex is SpeechException or InvalidDataException)
        {
            // The user asked for this voice: a version without it is not made (the first make goes on silent instead).
            throw new VideoException("The voice could not be made: " + ex.Message, ex) { Code = "speech" };
        }
        finally
        {
            // Online parts made before a failure (or a cancel) were paid for: counted either way.
            if (cost > 0)
            {
                spent.Add(new VideoCost { VideoId = id, Step = PipelineSteps.Speech, Usd = cost, Exact = false });
            }
        }
    }

    /// <summary>The kept scene images go where the engine looks for them (the project's public/ folder and its root), nothing of the old engine.</summary>
    private static void PlaceImages(VersionSource source, string project)
    {
        var kept = Path.Combine(source.Folder, VideoFolders.Source);
        Directory.CreateDirectory(project);
        var publicFolder = Path.Combine(kept, "public");
        if (Directory.Exists(publicFolder))
        {
            VideoAssembler.CopyProject(publicFolder, Path.Combine(project, "public"));
        }

        foreach (var image in source.Input.Scenes.Select(s => s.Image).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal))
        {
            var file = Path.Combine(kept, image);
            if (File.Exists(file) && !Path.IsPathRooted(image) && !image.Contains("..", StringComparison.Ordinal))
            {
                File.Copy(file, Path.Combine(project, image), overwrite: true);
            }
        }
    }

    /// <summary>The warnings about the images of the version a new one starts from (the images are the same).</summary>
    private static IEnumerable<string> ImageWarnings(VideoRecord record) =>
        record.Warnings.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(w => w is VideoWarnings.ImagesFailed or VideoWarnings.SamplesNone);

    private void Ready(IVideoEngine engine)
    {
        var status = engine.Status();
        if (!status.Ready)
        {
            logger.LogWarning("New version not started: {Engine} is not installed (missing: {Missing})", engine.Name, string.Join(", ", status.Missing));
            throw new VideoException($"{engine.Name} is not installed (missing: {string.Join(", ", status.Missing)}).") { Code = "missing" };
        }
    }
}
