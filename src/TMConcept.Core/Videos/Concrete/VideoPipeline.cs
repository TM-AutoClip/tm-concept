using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Engines;
using TMConcept.Core.Images;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Runs;
using TMConcept.Core.Scenes;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Videos;

/// <summary>
/// Concept → video (plan §6.1): 0 the engine and ffmpeg must be installed (checked before anything is paid for) · A prepare (scenes, no
/// model; the placeholders of the concept text are read: pauses and speed for the voice, transitions for the scenes) · B narration per scene
/// with the profile's narration step (<see cref="SceneNarrator"/>, the same as a new version; cached; off = silent video with a warning, a
/// chosen voice that fails stops the video) · D images (the profile's images step: off = no image call at all, a failure stops the
/// video before the code call, and an engine that cannot run here at all (<see cref="IImageEngine.Problem"/>) stops it at A, before the paid
/// prompt call; the prompts are written by the video code model, one prompt per scene with its number is checked and repaired once (a cut-off answer keeps its complete prompts); generated
/// images and their prompts are kept per concept until the video is made, so a retry does not pay for them again) · C timing in C# after the
/// clips and images (a pause lengthens its scene) · E
/// video code from the model, safety check, one repair · F render (processor only), one repair with the error text · H mux into the work
/// folder, then the finished file is moved into the library's version folder (<see cref="VideoFolders"/>); subtitles (.srt always, burned
/// ASS when chosen), the narration text (always), the engine project as it rendered and the scene clips (for a later re-render) ·
/// I thumbnail, video and cost rows (with the concept's share of its generation run as a line of its own, outside the video's total),
/// concept marked produced. Every paid call is counted in the cost, also when it fails (a cut-off code answer is billed). A failed or
/// canceled run leaves nothing half-written in the library and removes its work files (a rejected code is kept there as text for
/// reading); what it paid is kept as a <see cref="RunKind.FailedVideo"/> run, so the month's spending counts it. The code, render, mux and
/// version steps are <see cref="VideoAssembler"/>'s, shared with the later versions (<see cref="VideoRemaker"/>). The progress names every step
/// of the plan, the scenes done, the code call's tokens and what was paid so far (<see cref="VideoStepProgress"/>, the one list of the costs).
/// </summary>
public sealed class VideoPipeline(IEnumerable<IVideoEngine> engines, SpeechService speech, SceneNarrator narrator, IEnumerable<IImageEngine> images, StepModelResolver resolver,
    IChatClientFactory chats, ICostCalculator costs, VideoAssembler assembler, IConceptStore concepts, IRunStore runs,
    IAppPaths paths, IGpuCoordinator gpu, TimeProvider clock, ILogger<VideoPipeline> logger)
{
    /// <summary>Work folders of runs older than this are removed by <see cref="CleanWork"/>.</summary>
    public static readonly TimeSpan WorkKeep = TimeSpan.FromDays(7);

    // In the order of the work (VideoSteps.All): the timing needs the clips and the images, so it comes after both.
    private static readonly (string Step, double Weight)[] Weights =
    [
        (VideoSteps.Prepare, 2), (VideoSteps.Speech, 20), (VideoSteps.Images, 15), (VideoSteps.Timing, 1), (VideoSteps.Code, 15), (VideoSteps.Render, 40),
        (VideoSteps.Mux, 5), (VideoSteps.Save, 2),
    ];

    /// <summary>Makes the video.</summary>
    public async Task<VideoRecord> RunAsync(VideoJob job, IProgress<VideoProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var watch = Stopwatch.StartNew();
        var profile = job.Profile;
        var options = job.Options;
        var engine = engines.FirstOrDefault(e => e.Kind == options.Engine) ?? throw new VideoException($"Engine {options.Engine} is not available.") { Code = "render" };

        // 0. Nothing is paid for before the engine can render: a missing Node / ffmpeg / package / browser used to show up only after the
        // narration, images and code were bought.
        var status = engine.Status();
        if (!status.Ready)
        {
            logger.LogWarning("Video not started: {Engine} is not installed (missing: {Missing})", engine.Name, string.Join(", ", status.Missing));
            throw new VideoException($"{engine.Name} is not installed (missing: {string.Join(", ", status.Missing)}).") { Code = "missing" };
        }

        var id = Guid.NewGuid();
        var warnings = new List<string>();

        // The profile's narration and image choices (its choices on the video page). Off = no voice / no image call at all, a warning at
        // most. A chosen model that cannot be made ready is an unexpected failure and stops the video (the developer's rule of 16 Nov 2026:
        // 0.1.15 went on without images when sd-cli could not start, and the user paid for a code call of a video he did not want).
        var speechChoice = options.Speech ? await resolver.ChoiceAsync(PipelineSteps.Speech, profile.Id, cancellationToken).ConfigureAwait(false) : null;
        var imageChoice = options.Images == ImageSources.Model ? await resolver.ChoiceAsync(PipelineSteps.Images, profile.Id, cancellationToken).ConfigureAwait(false) : null;
        var speechOn = !StepModelResolver.IsOff(speechChoice);
        var imagesOn = !StepModelResolver.IsOff(imageChoice);
        logger.LogInformation("Video {Id} started: concept {Concept}, {Engine}, code model {Provider}/{Model}, voice {Voice}, images {Images}", id, job.Concept.Id,
            engine.Name, job.CodeModel.Model.Provider, job.CodeModel.Model.Id, speechOn ? speechChoice!.Provider + "/" + speechChoice.Model : "off",
            imagesOn ? imageChoice!.Provider + "/" + imageChoice.Model : "off");
        var work = assembler.WorkFolder(id);
        var target = new VersionTarget();
        var steps = new VideoStepProgress(
            Weights.Where(w => (w.Step != VideoSteps.Speech || speechOn) && (w.Step != VideoSteps.Images || imagesOn)).ToList(), progress);

        try
        {
            return await assembler.GuardAsync(profile.Id, job.CodeModel, work, target, steps, watch, () => MakeAsync(job, engine, speechChoice, imageChoice,
                imagesOn, id, warnings, work, target, steps, watch, cancellationToken)).ConfigureAwait(false);
        }
        catch (VideoException ex) when (warnings.Count > 0)
        {
            // The warnings gathered before the failure are still shown with it (narration off…).
            throw new VideoException(ex.Message, ex.InnerException ?? ex) { Code = ex.Code, SpentUsd = ex.SpentUsd, Warnings = [.. warnings] };
        }
    }

    private async Task<VideoRecord> MakeAsync(VideoJob job, IVideoEngine engine, StepModelChoice? speechChoice, StepModelChoice? imageChoice, bool imagesOn,
        Guid id, List<string> warnings, string work, VersionTarget target, VideoStepProgress steps, Stopwatch watch, CancellationToken cancellationToken)
    {
        var profile = job.Profile;
        var options = job.Options;

        // A. prepare: the chosen voice and image model are made ready first (inside the run, so its Prepare row shows the failure).
        steps.Report(VideoSteps.Prepare, 0);
        SpeechSetup? voice = null;
        if (!StepModelResolver.IsOff(speechChoice))
        {
            try
            {
                voice = await speech.SetupAsync(speechChoice!, profile.ContentLanguage, cancellationToken).ConfigureAwait(false);
            }
            catch (SpeechException ex)
            {
                logger.LogError(LogEvents.OnScreen, "The narration model is not ready ({Code}); the video is stopped", ex.Code);
                throw new VideoException(ex.Message, ex) { Code = "speech" };
            }
        }

        ResolvedModel? imageModel = null;
        if (!StepModelResolver.IsOff(imageChoice))
        {
            try
            {
                imageModel = await resolver.ResolveAsync(PipelineSteps.Images, imageChoice, cancellationToken).ConfigureAwait(false);
            }
            catch (ModelNotReadyException ex)
            {
                logger.LogError(LogEvents.OnScreen, "The image model is not ready (no {Missing}); the video is stopped", ex.Missing);
                throw new VideoException(ex.Message, ex) { Code = "images" };
            }

            // A local engine that cannot start here (no Visual C++ runtime, no driver for the chosen build) stops the video now, before the
            // paid image prompt call (0.1.15's AMD tester paid for it, then sd-cli ended with 0xC0000135).
            if (images.FirstOrDefault(e => e.Code == imageModel.Model.Provider)?.Problem(imageModel) is { } problem)
            {
                logger.LogError(LogEvents.OnScreen, "The image engine cannot run on this computer ({Code}: {Detail}); the video is stopped", problem.Code,
                    problem.Detail);
                throw new VideoException(problem.Detail) { Code = problem.Code };
            }
        }

        var speechOn = voice is not null;
        if (!speechOn && Styles.VideoStyleRules.WithoutVoice(options.Style) is { } shown && !ReferenceEquals(shown, options.Style))
        {
            // A spoken closing without a voice would only lengthen a silent scene (and be lost with subtitles off): this video's snapshot
            // shows it on the closing card instead, so the overlay, the code's input and a later version agree.
            logger.LogWarning("The template's closing is spoken but narration is off; it is shown on the closing card instead");
            options.Style = shown;
        }

        var split = SceneSplitter.Split(job.Concept.Content);
        if (split.Scenes.Count == 0 || split.Scenes.All(s => s.Narration.Length == 0))
        {
            throw new VideoException("The concept has no scenes: " + string.Join(" ", split.Problems)) { Code = "scenes" };
        }

        Directory.CreateDirectory(work);

        // The template's spoken closing: its sentence (in the video's language) ends the last scene's narration before the voice reads it, so
        // the timing follows the real clip, the subtitles show it and a later version keeps it. Images are made from the scenes as written.
        var closing = Styles.VideoStyleRules.Closing(options.Style, profile.ContentLanguage);
        var scenes = closing is { Voice: true } ? Styles.ClosingNarration.Append(split.Scenes, closing.Sentence) : split.Scenes;
        steps.Finish(VideoSteps.Prepare);

        // B. narration
        var clips = new List<SpeechClip?>();
        if (!speechOn)
        {
            // No voice by choice: the video is silent, with a warning.
            warnings.Add(VideoWarnings.SpeechOff);
            logger.LogWarning("Narration is off; the video will be silent");
        }
        else
        {
            try
            {
                var count = scenes.Count;
                var pacing = Styles.VideoStyleRules.Pacing(options.Style);
                steps.Report(VideoSteps.Speech, 0, done: 0, total: count);
                for (var i = 0; i < count; i++)
                {
                    // The scene's script: its pauses and speed placeholders are applied exactly as in a new version.
                    var clip = await SceneVoiceAsync(voice!, profile.ContentLanguage, i + 1, scenes[i].Script, work, pacing, steps, id, cancellationToken)
                        .ConfigureAwait(false);
                    clips.Add(clip);
                    steps.Report(VideoSteps.Speech, (i + 1.0) / count, done: i + 1, total: count);
                }

                warnings.Add(VideoWarnings.AiVoice);
                var skipped = clips.OfType<SpeechClip>().SelectMany(c => c.SkippedWords).Distinct(StringComparer.Ordinal).Count();
                if (skipped > 0)
                {
                    // Only the count is logged: the words come from the concept.
                    warnings.Add(VideoWarnings.SpeechSkipped);
                    logger.LogWarning("The voice left out {Count} words it does not know; the subtitles still show them", skipped);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not Providers.ProviderException and not VideoException)
            {
                // A chosen voice that fails stops the video (16 Nov 2026; 0.1.15 went on silent). An online voice's provider error goes
                // through as it is: the run's guard names it with the provider's own text.
                logger.LogError(LogEvents.OnScreen, ex, "Narration failed; the video is stopped");
                throw new VideoException(ex.Message, ex) { Code = ex is MemoryShortfallException ? "memory" : "speech" };
            }
            finally
            {
                // The voice is not needed until the next video: its worker ends, so the card (a Python voice on CUDA) and the main memory are
                // free for the images.
                await speech.ReleaseAsync(voice!, CancellationToken.None).ConfigureAwait(false);
            }

            steps.Finish(VideoSteps.Speech);
        }

        // D. images
        var (width, height) = RenderInputBuilder.Size(profile.Format);
        List<string> sceneImages = [];
        IReadOnlyList<string> imagePrompts = [];
        if (imagesOn)
        {
            (sceneImages, imagePrompts) = await ImagesAsync(job, imageModel, split.Scenes, work, steps, id, width, height, cancellationToken).ConfigureAwait(false);
            if (sceneImages.Count < split.Scenes.Count)
            {
                // Chosen images and fewer pictures than scenes: a scene would show nothing (stopped before the paid code call).
                logger.LogError(LogEvents.OnScreen, "{Images} scene images for {Scenes} scenes; the video is stopped", sceneImages.Count, split.Scenes.Count);
                throw new VideoException(string.Create(CultureInfo.InvariantCulture, $"{sceneImages.Count} scene images were made for {split.Scenes.Count} scenes."))
                {
                    Code = "images",
                };
            }

            steps.Finish(VideoSteps.Images);
        }

        // C. timing, after the images: a scene lasts as long as its clip (a silent one as long as its text takes to say).
        steps.Report(VideoSteps.Timing, 0);
        var sources = scenes.Select((s, i) => new RenderSceneSource(s.Visual, s.Narration, sceneImages.ElementAtOrDefault(i) ?? string.Empty,
            clips.ElementAtOrDefault(i)?.DurationMs ?? VideoRemaker.SilentMs(s.Script, profile.ContentLanguage), s.Transition)).ToList();
        var input = RenderInputBuilder.Build(job.Concept.Title, profile.ContentLanguage, sources, width, height, 30,
            endHoldMs: Styles.VideoStyleRules.EndHoldMs(options.Style, profile.ContentLanguage));
        steps.Finish(VideoSteps.Timing);

        // E + F. code (+ one repair after a rejected check), render (+ one repair with the error text)
        var rendered = await assembler.CodeAndRenderAsync(engine, job.CodeModel, options, input, work, id, steps, cancellationToken,
            new PlanContext(clips.Select(c => c?.Spans).ToList(), imagePrompts,
                Plans.PlanDrawer.Draws(engine.Kind, options.Style) && sceneImages.Count > 0 ? await PointsModelAsync(profile.Id, cancellationToken).ConfigureAwait(false) : null))
            .ConfigureAwait(false);
        warnings.AddRange(rendered.Warnings());

        // H + I. mux into the work folder, then the version folder gets only finished files; the record.
        var folder = Path.Combine(LibraryFolder(profile, job.Concept), VideoFolders.FirstVersion);
        var record = await assembler.SaveAsync(new VersionSave(id, profile, job.Concept.Id, job.Concept.Title, work, rendered, clips, target, folder, options,
            engine.Name, job.CodeModel.Model.Provider + "/" + job.CodeModel.Model.Id,
            speechOn ? voice!.Model.Provider + "/" + voice.Model.Id + ":" + voice.Voice : string.Empty,
            imageModel is not null ? imageModel.Model.Provider + "/" + imageModel.Model.Id : imagesOn ? options.Images : string.Empty,
            Plans.PlanDrawer.Draws(engine.Kind, options.Style) ? "plan-" + Plans.PlanPromptBuilder.Version : VideoCodePrompt.Version, warnings, steps.Spent, await ConceptShareAsync(job.Concept, id).ConfigureAwait(false), watch)
        {
            // Each scene's text with its placeholders and the transition into it: a new version is edited from this.
            Script = scenes.Select(s => VideoRemaker.WithTransition(s.Transition, s.Script, profile.ContentLanguage)).ToList(),
        }, steps,
            cancellationToken).ConfigureAwait(false);
        await concepts.SetStatusAsync([job.Concept.Id], ConceptStatus.Produced, CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Video {Id} made in {Seconds:0} s ({Engine}, {Usd} USD)", id, watch.Elapsed.TotalSeconds, engine.Name, record.TotalUsd);
        assembler.TryDelete(work);
        assembler.TryDelete(SceneImageRoot(job.Concept.Id));
        return record;
    }

    /// <summary>
    /// The concept's share of the run it was generated in (that run's cost ÷ the concepts it kept), as a cost line of its own: shown first in
    /// the video's cost details, but not in the video's total (the run is counted once, in the month's spending of the concept runs).
    /// Nothing when the concept has no run (made by hand) or the run is gone. The AI edits of the concept that no earlier video recorded
    /// follow as lines of their own (<see cref="VideoCostBreakdown.ConceptEdits"/>), outside the total too.
    /// </summary>
    private async Task<IReadOnlyList<VideoCost>> ConceptShareAsync(Concept concept, Guid videoId)
    {
        var lines = new List<VideoCost>();
        if (concept.RunId is { } runId && await runs.GetAsync(runId, CancellationToken.None).ConfigureAwait(false) is { } run)
        {
            var share = VideoCostBreakdown.ConceptShare(run.ActualUsd, run.Accepted);
            if (share > 0)
            {
                lines.Add(new VideoCost { VideoId = videoId, Step = PipelineSteps.Concepts, RunId = runId, Usd = share, Exact = run.CostExact });
            }
        }

        // The AI edits of the concept no earlier video recorded (0.1.25: edit runs name their concept), one line each, outside the total
        // like the share. A free edit (a local model) stores no line.
        foreach (var edit in await runs.ConceptEditsNotInVideosAsync(concept.Id, CancellationToken.None).ConfigureAwait(false))
        {
            if (edit.ActualUsd > 0)
            {
                lines.Add(new VideoCost { VideoId = videoId, Step = VideoCostBreakdown.ConceptEdits, RunId = edit.Id, Usd = edit.ActualUsd, Exact = edit.CostExact });
            }
        }

        return lines;
    }

    /// <summary>
    /// The end of the video queue: the local model still on the graphics card (a language model, an image model, a voice) is unloaded, so the
    /// card is free again for the user's own programs; nothing when another page uses it. A failure is only logged by the coordinator.
    /// </summary>
    public Task FreeCardAsync(CancellationToken cancellationToken) => gpu.FreeAsync(cancellationToken);

    /// <summary>
    /// Called once at start, before any video runs (no run can own a work folder then). A run's work folder left by an app that was closed
    /// or killed mid-run is an orphan: it is emptied at once, except a rejected code kept for reading (the folder goes with it after
    /// <see cref="WorkKeep"/>). Scene images that were paid for stay for a retry until <see cref="WorkKeep"/>. 0.1.28: also what
    /// <see cref="CleanOld"/> takes, and the listen and sample-render files (nothing plays or renders them at start).
    /// </summary>
    public void CleanWork()
    {
        var runs = Path.Combine(paths.WorkDirectory, "videos");
        if (Directory.Exists(runs))
        {
            foreach (var folder in Directory.EnumerateDirectories(runs).Where(f => clock.GetUtcNow() - Directory.GetLastWriteTimeUtc(f) <= WorkKeep))
            {
                assembler.CleanFailed(folder, new VersionTarget());
            }
        }

        CleanOld();
        assembler.TryDelete(Path.Combine(paths.WorkDirectory, "listen"));
        assembler.TryDelete(Path.Combine(paths.WorkDirectory, "sample"));
    }

    /// <summary>
    /// At start and at the end of the video queue (0.1.28; until then only at start, and the cached narration never): what is older than its
    /// keep time and so no retry uses: run folders, scene images and cached narration older than <see cref="WorkKeep"/>, and voice sample
    /// work files older than a day (a crash left them; a request deletes its own right after it). A file that cannot be removed now is
    /// logged and tried again next time (the storage page lists it meanwhile).
    /// </summary>
    public void CleanOld()
    {
        var now = clock.GetUtcNow();
        foreach (var kind in new[] { "videos", "scene-images" })
        {
            var root = Path.Combine(paths.WorkDirectory, kind);
            if (Directory.Exists(root))
            {
                foreach (var folder in Directory.EnumerateDirectories(root).Where(f => now - Directory.GetLastWriteTimeUtc(f) > WorkKeep))
                {
                    assembler.TryDelete(folder);
                }
            }
        }

        DeleteOlder(Path.Combine(paths.WorkDirectory, "speech-cache"), now - WorkKeep);
        DeleteOlder(Path.Combine(paths.WorkDirectory, "voices"), now - TimeSpan.FromDays(1));
    }

    /// <summary>Deletes the files of <paramref name="folder"/> last written before <paramref name="before"/>.</summary>
    private void DeleteOlder(string folder, DateTimeOffset before)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder).Where(f => File.GetLastWriteTimeUtc(f) < before.UtcDateTime))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "An old work file could not be removed yet");
            }
        }
    }

    /// <summary>
    /// One scene's clip (<see cref="SceneNarrator"/>); what an online voice charged for it is spent at once, also when the clip fails or is
    /// canceled halfway (the parts made before were paid for).
    /// </summary>
    private async Task<SpeechClip?> SceneVoiceAsync(SpeechSetup voice, string language, int number, string script, string work, NarrationPacing pacing,
        VideoStepProgress steps, Guid id, CancellationToken cancellationToken)
    {
        decimal cost = 0;
        try
        {
            return await narrator.VoiceAsync(voice, language, number, script, work, pacing, usd => cost += usd, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (cost > 0)
            {
                steps.Spend(new VideoCost { VideoId = id, Step = PipelineSteps.Speech, Usd = cost, Exact = false });
            }
        }
    }

    /// <summary>
    /// The scene images of the chosen model. Any failure stops the video (<see cref="VideoException"/> "images").
    /// </summary>
    private async Task<(List<string> Names, IReadOnlyList<string> Prompts)> ImagesAsync(VideoJob job, ResolvedModel? imageModel, IReadOnlyList<Scene> scenes,
        string work, VideoStepProgress steps, Guid id, int width, int height, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        IReadOnlyList<string> prompts = [];
        var publicFolder = Path.Combine(work, "project", "public");
        void Place(string source, string name)
        {
            Directory.CreateDirectory(publicFolder);
            File.Copy(source, Path.Combine(publicFolder, name), overwrite: true);
            File.Copy(source, Path.Combine(work, "project", name), overwrite: true);
        }

        try
        {
            if (imageModel is not null)
            {
                var engine = images.FirstOrDefault(e => e.Code == imageModel.Model.Provider)
                    ?? throw new VideoException($"{imageModel.Model.Provider} has no image engine.") { Code = "images" };
                var visuals = scenes.Select(s => s.Visual.Length > 0 ? s.Visual : s.Narration).ToList();

                // Prompts and images of this concept are kept until its video is made: a retry after a later failure reuses them.
                var drawStyle = job.Options.Style?.DrawStyle;
                var cache = Path.Combine(SceneImageRoot(job.Concept.Id), CacheKey(job.Profile.Name, imageModel.Model, width, height, visuals, drawStyle));
                Directory.CreateDirectory(cache);
                var promptFile = Path.Combine(cache, "prompts.txt");
                var styleFile = Path.Combine(cache, "style.txt");
                if (File.Exists(promptFile) && (await File.ReadAllLinesAsync(promptFile, cancellationToken).ConfigureAwait(false)) is { } kept && kept.Length == visuals.Count)
                {
                    prompts = kept;
                    logger.LogInformation("Scene image prompts reused from an earlier attempt");
                    if (drawStyle is Styles.DrawStyle.Auto && File.Exists(styleFile))
                    {
                        Choose(job.Options, await File.ReadAllTextAsync(styleFile, cancellationToken).ConfigureAwait(false));
                    }
                }
                else
                {
                    var (made, chosen) = await ImagePromptsAsync(job, visuals, drawStyle, steps, id, cancellationToken).ConfigureAwait(false);
                    prompts = made;
                    await File.WriteAllLinesAsync(promptFile, prompts.Select(p => p.ReplaceLineEndings(" ")), cancellationToken).ConfigureAwait(false);
                    if (drawStyle is Styles.DrawStyle.Auto)
                    {
                        await File.WriteAllTextAsync(styleFile, chosen, cancellationToken).ConfigureAwait(false);
                        Choose(job.Options, chosen);
                    }
                }

                steps.Report(VideoSteps.Images, 0, done: 0, total: prompts.Count);

                // One graphics card lease for all the scene images (0.1.17): the prompt model's lease ended with its answer, so a local
                // prompt model is unloaded here before the image model takes the card, and no other page's local model comes in between.
                var lease = await gpu.AcquireAsync(imageModel.Model, cancellationToken).ConfigureAwait(false);
                await using var leaseScope = lease.ConfigureAwait(false);
                for (var i = 0; i < prompts.Count; i++)
                {
                    var file = Path.Combine(cache, "scene-" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".png");
                    if (!File.Exists(file))
                    {
                        var result = await engine.GenerateAsync(new ImageRequest(prompts[i], width, height, file, imageModel), cancellationToken).ConfigureAwait(false);
                        logger.LogInformation("Scene image {Number} of {Count} made with {Provider}/{Model}", i + 1, prompts.Count, imageModel.Model.Provider,
                            imageModel.Model.Id);
                        steps.Spend(new VideoCost { VideoId = id, Step = PipelineSteps.Images, Usd = result.CostUsd, Exact = result.CostUsd == 0 });
                        if (!File.Exists(file))
                        {
                            throw new VideoException(string.Create(CultureInfo.InvariantCulture,
                                $"{imageModel.Model.Name} made no image file for scene {i + 1}.")) { Code = "images" };
                        }
                    }

                    Place(file, Path.GetFileName(file));
                    names.Add(Path.GetFileName(file));
                    steps.Report(VideoSteps.Images, (i + 1.0) / prompts.Count, done: i + 1, total: prompts.Count);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not Providers.ProviderException and not VideoException)
        {
            // Any failure of a chosen image step stops the video before the code call (16 Nov 2026: 0.1.15 went on without images when sd-cli
            // could not start and the user paid for a video he did not want). The engine's own message (sd-cli's exit code and output) is
            // the reason the user sees. A provider's error goes through as it is and is named with the provider's text.
            logger.LogError(LogEvents.OnScreen, ex, "Scene images failed; the video is stopped");
            throw new VideoException(ex.Message, ex) { Code = ex is MemoryShortfallException ? "memory" : "images" };
        }

        return (names, prompts);
    }

    /// <summary>
    /// The image points model of a video drawn by the app (0.1.33, optional): null when it is off or cannot be made ready (the plan's own
    /// guesses are used then; an overlay never stops a video).
    /// </summary>
    private async Task<ResolvedModel?> PointsModelAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var choice = await resolver.ChoiceAsync(PipelineSteps.ImagePoints, profileId, cancellationToken).ConfigureAwait(false);
        if (StepModelResolver.IsOff(choice))
        {
            return null;
        }

        try
        {
            return await resolver.ResolveAsync(PipelineSteps.ImagePoints, choice, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelNotReadyException ex)
        {
            logger.LogWarning("The image points model is not ready (no {Missing}); the plan's own guesses are used", ex.Missing);
            return null;
        }
    }

    /// <summary>
    /// The scene image prompts from the video code model (one model less to choose), one per scene with its number (20 Nov 2026). An answer
    /// without one readable prompt is asked again as it was (at most twice, <see cref="JsonAnswerRetry"/>); an answer that does not fit the
    /// scenes (<see cref="ImagePromptBuilder.Check"/>: the count, a scene without a prompt, a wrong number, an empty prompt) gets ONE repair
    /// request with those findings, and when that does not fit either the video stops with them ("images"). A cut-off answer keeps its
    /// complete items and goes to the same check. Every call is paid and counted. 0.1.32: a template without a style
    /// (<see cref="Styles.DrawStyle.Auto"/>) lets this call choose one; the second value is the chosen preset's key or the model's own words
    /// (empty otherwise; a repair answer that names one replaces the first).
    /// </summary>
    private async Task<(IReadOnlyList<string> Prompts, string Style)> ImagePromptsAsync(VideoJob job, IReadOnlyList<string> visuals, Styles.DrawStyle? drawStyle,
        VideoStepProgress steps, Guid id, CancellationToken cancellationToken)
    {
        var promptModel = job.CodeModel;
        var choose = drawStyle is Styles.DrawStyle.Auto;
        var chat = chats.Create(promptModel.Model, promptModel.Key.Secret);
        async Task<string> AskAsync(string user, CancellationToken token)
        {
            try
            {
                var answer = await chat.SendAsync(new ChatRequest(ImagePromptBuilder.System, user, Schema: ImagePromptBuilder.Schema(choose), MaxOutputTokens: 4000,
                    Effort: promptModel.Effort), token).ConfigureAwait(false);
                steps.Spend(new VideoCost
                {
                    VideoId = id,
                    Step = PipelineSteps.ImagePromptCost,
                    Usd = costs.Exact(promptModel.Model, answer.Usage, answer.ReportedCostUsd),
                    Exact = true,
                });
                return answer.Text;
            }
            catch (Providers.ProviderException ex) when (ex.Code is "truncated" or "incomplete")
            {
                // Letting a cut answer go up named the video code as cut off, a step that never ran (the 0.1.16 review).
                logger.LogWarning("The image prompt answer was cut off ({Code}); its complete prompts go to the check", ex.Code);
                if (ex.ChargedUsage is { } charged)
                {
                    steps.Spend(new VideoCost { VideoId = id, Step = PipelineSteps.ImagePromptCost, Usd = costs.Exact(promptModel.Model, charged), Exact = true });
                }

                return ex.PartialText;
            }
            catch (Providers.ProviderException ex) when (ex.ChargedUsage is { } charged)
            {
                steps.Spend(new VideoCost { VideoId = id, Step = PipelineSteps.ImagePromptCost, Usd = costs.Exact(promptModel.Model, charged), Exact = true });
                throw;
            }
        }

        var first = await JsonAnswerRetry.RunAsync("Scene image prompts", async (_, token) =>
        {
            var text = await AskAsync(ImagePromptBuilder.User(job.Profile.Name, visuals, drawStyle), token).ConfigureAwait(false);
            return ImagePromptBuilder.Read(text).Count > 0 ? new[] { text } : null;
        }, logger, cancellationToken).ConfigureAwait(false);
        var items = ImagePromptBuilder.Read(first[0]);
        var style = choose ? StyleWords(first[0]) : string.Empty;
        var problems = ImagePromptBuilder.Check(items, visuals.Count);
        if (problems.Count > 0)
        {
            logger.LogWarning("The image prompts do not fit the {Count} scenes ({Problems}); asking for one repair", visuals.Count, string.Join("; ", problems));
            var repaired = await AskAsync(ImagePromptBuilder.Repair(job.Profile.Name, visuals, drawStyle, problems), cancellationToken).ConfigureAwait(false);
            items = ImagePromptBuilder.Read(repaired);
            if (choose && StyleWords(repaired) is { Length: > 0 } again)
            {
                style = again;
            }

            problems = ImagePromptBuilder.Check(items, visuals.Count);
            if (problems.Count > 0)
            {
                logger.LogError(LogEvents.OnScreen, "The image prompts still do not fit the scenes after the repair ({Problems}); the video is stopped",
                    string.Join("; ", problems));
                throw new VideoException("The image prompts still did not fit the scenes after one repair: " + string.Join("; ", problems)) { Code = "images" };
            }
        }

        logger.LogInformation("Scene image prompts: {Count}, one per scene", items.Count);
        return (ImagePromptBuilder.Prompts(items, visuals.Count), style);
    }

    /// <summary>The style an answer chose: a preset's key, or the model's own words.</summary>
    private static string StyleWords(string answer)
    {
        var (preset, words) = ImagePromptBuilder.ReadStyle(answer);
        return preset is { } chosen ? Styles.DrawStyles.Of(chosen).Key : words;
    }

    /// <summary>
    /// Writes the style chosen for a template without one into this video's snapshot (0.1.32): a preset's key becomes its
    /// <see cref="Styles.VideoStyle.DrawStyle"/> (so the code gets its drawing, craft and texture), the model's own words go to
    /// <see cref="Styles.VideoStyle.ChosenStyle"/>. Nothing chosen leaves the snapshot on Auto (the code then chooses).
    /// </summary>
    private void Choose(VideoOptions options, string chosen)
    {
        if (options.Style is not { } snapshot || chosen.Trim().Length == 0)
        {
            return;
        }

        if (Styles.DrawStyles.FindKey(chosen) is { } preset)
        {
            snapshot.DrawStyle = preset.Style;
            logger.LogInformation("The style chosen for this video: {Style}", preset.Key);
        }
        else
        {
            snapshot.ChosenStyle = Text.TextClean.OneLine(chosen, ImagePromptBuilder.StyleMax);
            logger.LogInformation("The style chosen for this video: its own description ({Length} characters)", snapshot.ChosenStyle.Length);
        }
    }

    private string SceneImageRoot(Guid conceptId) => SceneImageFolder(paths.WorkDirectory, conceptId);

    /// <summary>The kept scene images of a concept (<c>work/scene-images/&lt;concept&gt;</c>; deleted with the concept or its profile).</summary>
    public static string SceneImageFolder(string workDirectory, Guid conceptId) => Path.Combine(workDirectory, "scene-images", conceptId.ToString("N"));

    /// <summary>
    /// The key of a concept's kept image prompts and images: another channel, model, size, scene text, template drawing style or prompt format
    /// (<see cref="ImagePromptBuilder.Version"/>) makes new ones.
    /// </summary>
    private static string CacheKey(string channel, CatalogModel model, int width, int height, IReadOnlyList<string> visuals, Styles.DrawStyle? drawStyle)
    {
        var parts = new[] { "prompts:" + ImagePromptBuilder.Version, channel, model.Provider, model.Id, width.ToString(CultureInfo.InvariantCulture),
            height.ToString(CultureInfo.InvariantCulture) }.Concat(visuals);
        if (drawStyle is { } style)
        {
            parts = parts.Append("style:" + RenderStyleBuilder.Name(style));
        }

        var text = string.Join('\n', parts);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    private string LibraryFolder(Profile profile, Concept concept)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"{clock.GetLocalNow():yyyy-MM-dd}-{concept.Number}-{LibraryPaths.SafeName(concept.Title)}");
        var folder = Path.Combine(paths.LibraryDirectory, LibraryPaths.SafeName(profile.Name), name);
        for (var i = 2; Directory.Exists(Path.Combine(folder, VideoFolders.FirstVersion)); i++)
        {
            folder = Path.Combine(paths.LibraryDirectory, LibraryPaths.SafeName(profile.Name), name + "-" + i.ToString(CultureInfo.InvariantCulture));
        }

        return folder;
    }

}
