using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
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
/// model) · B narration per scene with the profile's narration step (cached; off or failed = silent video with a warning) · C timing in C#
/// · D images (samples, or the profile's images step: off = no image call at all; the prompts are written by the video code model; generated
/// images and their prompts are kept per concept until the video is made, so a retry does not pay for them again) · E
/// video code from the model, safety check, one repair · F render (processor only), one repair with the error text · H mux into the work
/// folder, then the finished file is moved into the library's version folder (<see cref="VideoFolders"/>); subtitles (.srt always, burned
/// ASS when chosen), the narration text (always), the engine project as it rendered and the scene clips (for a later re-render) ·
/// I thumbnail, video and cost rows (with the concept's share of its generation run as a line of its own, outside the video's total),
/// concept marked produced. Every paid call is counted in the cost, also when it fails (a cut-off code answer is billed). A failed or
/// canceled run leaves nothing half-written in the library and removes its work files (a rejected code is kept there as text for
/// reading); what it paid is kept as a <see cref="RunKind.FailedVideo"/> run, so the month's spending counts it. The code, render, mux and
/// version steps are <see cref="VideoAssembler"/>'s, shared with the later versions (<see cref="VideoRemaker"/>).
/// </summary>
public sealed class VideoPipeline(IEnumerable<IVideoEngine> engines, SpeechService speech, IEnumerable<IImageEngine> images, StepModelResolver resolver,
    IChatClientFactory chats, ICostCalculator costs, VideoAssembler assembler, IConceptStore concepts, IProfileStore profiles, IRunStore runs,
    IAppPaths paths, TimeProvider clock, ILogger<VideoPipeline> logger)
{
    /// <summary>Work folders of runs older than this are removed by <see cref="CleanWork"/>.</summary>
    public static readonly TimeSpan WorkKeep = TimeSpan.FromDays(7);

    private static readonly (string Step, double Weight)[] Weights =
        [("prepare", 2), ("speech", 20), ("timing", 1), ("images", 15), ("code", 15), ("render", 40), ("mux", 5), ("save", 2)];

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
        var spent = new List<VideoCost>();

        // The profile's narration and image models (its choices on the video page). A model that is not ready means a silent video or no
        // images, with a warning; an image step that is off makes no image call at all.
        SpeechSetup? voice = null;
        if (options.Speech)
        {
            try
            {
                voice = await speech.ResolveAsync(profile.Id, profile.ContentLanguage, cancellationToken).ConfigureAwait(false);
            }
            catch (SpeechException ex)
            {
                logger.LogError(ex, "The narration model is not ready ({Code}); the video will be silent", ex.Code);
                warnings.Add(VideoWarnings.SpeechFailed);
            }
        }

        ResolvedModel? imageModel = null;
        if (options.Images == ImageSources.Model)
        {
            try
            {
                imageModel = await resolver.ResolveOptionalAsync(PipelineSteps.Images, profile.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (ModelNotReadyException ex)
            {
                logger.LogError("The image model is not ready (no {Missing}); the video is made without images", ex.Missing);
                warnings.Add(VideoWarnings.ImagesFailed);
            }
        }

        var imagesOn = options.Images == ImageSources.Samples || imageModel is not null;
        logger.LogInformation("Video {Id} started: concept {Concept}, {Engine}, code model {Provider}/{Model}, voice {Voice}, images {Images}", id, job.Concept.Id,
            engine.Name, job.CodeModel.Model.Provider, job.CodeModel.Model.Id, voice is null ? "off" : voice.Model.Provider + "/" + voice.Model.Id,
            imageModel is null ? (imagesOn ? options.Images : "off") : imageModel.Model.Provider + "/" + imageModel.Model.Id);
        var work = assembler.WorkFolder(id);
        var target = new VersionTarget();
        var speechOn = voice is not null;
        var steps = new VideoStepProgress(Weights.Where(w => (w.Step != "speech" || speechOn) && (w.Step != "images" || imagesOn)).ToList(), progress);
        void Report(string step, double inside, string detail = "") => steps.Report(step, inside, detail);
        void Finish(string step) => steps.Finish(step);

        return await assembler.GuardAsync(profile.Id, job.CodeModel, work, target, spent, watch, async () =>
        {
            // A. prepare
            Report("prepare", 0);
            var split = SceneSplitter.Split(job.Concept.Content);
            if (split.Scenes.Count == 0 || split.Scenes.All(s => s.Narration.Length == 0))
            {
                throw new VideoException("The concept has no scenes: " + string.Join(" ", split.Problems)) { Code = "scenes" };
            }

            Directory.CreateDirectory(work);
            Finish("prepare");

            // B. narration
            var clips = new List<SpeechClip?>();
            if (!speechOn)
            {
                // No voice: off by choice, or its model could not be resolved (that warning is already there). Either way nothing is
                // spoken; 0.1.6 went on to the narration loop with a null voice when the model failed and threw ArgumentNullException.
                if (!warnings.Contains(VideoWarnings.SpeechFailed))
                {
                    warnings.Add(VideoWarnings.SpeechOff);
                    logger.LogWarning("Narration is off; the video will be silent");
                }
            }
            else
            {
                decimal speechCost = 0;
                try
                {
                    for (var i = 0; i < split.Scenes.Count; i++)
                    {
                        var narration = split.Scenes[i].Narration;
                        var clip = narration.Length == 0 ? null : await speech.SynthesizeAsync(voice!, profile.ContentLanguage, narration,
                            Path.Combine(work, "audio", (i + 1).ToString(CultureInfo.InvariantCulture) + ".wav"), cancellationToken).ConfigureAwait(false);
                        speechCost += clip?.CostUsd ?? 0;
                        clips.Add(clip);
                        Report("speech", (i + 1.0) / split.Scenes.Count);
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
                catch (Exception ex) when (ex is SpeechException or Providers.ProviderException or IOException or InvalidDataException)
                {
                    logger.LogError(ex, "Narration failed; the video will be silent");
                    warnings.Add(VideoWarnings.SpeechFailed);
                    clips.Clear();
                }
                finally
                {
                    // Online clips made before a failure (or a cancel) were paid for: they are counted either way.
                    if (speechCost > 0)
                    {
                        spent.Add(new VideoCost { VideoId = id, Step = PipelineSteps.Speech, Usd = speechCost, Exact = false });
                    }
                }

                Finish("speech");
            }

            // C. timing
            var (width, height) = RenderInputBuilder.Size(profile.Format);
            var sceneImages = !imagesOn ? [] : await ImagesAsync(job, imageModel, split.Scenes, work, warnings, spent, id, width, height, p => Report("images", p),
                cancellationToken).ConfigureAwait(false);
            var sources = split.Scenes.Select((s, i) => new RenderSceneSource(s.Visual, s.Narration, sceneImages.ElementAtOrDefault(i) ?? string.Empty,
                clips.ElementAtOrDefault(i)?.DurationMs ?? VideoCostEstimator.Milliseconds(s.Narration, profile.ContentLanguage))).ToList();
            var input = RenderInputBuilder.Build(job.Concept.Title, profile.ContentLanguage, sources, width, height, 30);
            Finish("timing");
            if (imagesOn)
            {
                Finish("images");
            }

            // E + F. code (+ one repair after a rejected check), render (+ one repair with the error text)
            var rendered = await assembler.CodeAndRenderAsync(engine, job.CodeModel, options.Subtitles, input, work, spent, id, steps, cancellationToken)
                .ConfigureAwait(false);
            if (rendered.Repaired)
            {
                warnings.Add(VideoWarnings.Repaired);
            }

            // H + I. mux into the work folder, then the version folder gets only finished files; the record.
            var folder = Path.Combine(LibraryFolder(profile, job.Concept), VideoFolders.FirstVersion);
            var record = await assembler.SaveAsync(new VersionSave(id, profile, job.Concept.Id, job.Concept.Title, work, rendered, clips, target, folder, options,
                engine.Name, job.CodeModel.Model.Provider + "/" + job.CodeModel.Model.Id,
                speechOn && !warnings.Contains(VideoWarnings.SpeechFailed) ? voice!.Model.Provider + "/" + voice.Model.Id + ":" + voice.Voice : string.Empty,
                imageModel is not null ? imageModel.Model.Provider + "/" + imageModel.Model.Id : imagesOn ? options.Images : string.Empty,
                VideoCodePrompt.Version, warnings, spent, await ConceptShareAsync(job.Concept, id).ConfigureAwait(false), watch), steps,
                cancellationToken).ConfigureAwait(false);
            await concepts.SetStatusAsync([job.Concept.Id], ConceptStatus.Produced, CancellationToken.None).ConfigureAwait(false);
            logger.LogInformation("Video {Id} made in {Seconds:0} s ({Engine}, {Usd} USD)", id, watch.Elapsed.TotalSeconds, engine.Name, record.TotalUsd);
            assembler.TryDelete(work);
            assembler.TryDelete(SceneImageRoot(job.Concept.Id));
            return record;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The concept's share of the run it was generated in (that run's cost ÷ the concepts it kept), as a cost line of its own: shown first in
    /// the video's cost details, but not in the video's total (the run is counted once, in the month's spending of the concept runs).
    /// Nothing when the concept has no run (made by hand) or the run is gone.
    /// </summary>
    private async Task<IReadOnlyList<VideoCost>> ConceptShareAsync(Concept concept, Guid videoId)
    {
        if (concept.RunId is not { } runId || await runs.GetAsync(runId, CancellationToken.None).ConfigureAwait(false) is not { } run)
        {
            return [];
        }

        var share = VideoCostBreakdown.ConceptShare(run.ActualUsd, run.Accepted);
        return share <= 0 ? [] : [new VideoCost { VideoId = videoId, Step = PipelineSteps.Concepts, RunId = runId, Usd = share, Exact = run.CostExact }];
    }

    /// <summary>
    /// Called once at start, before any video runs (no run can own a work folder then). A run's work folder left by an app that was closed
    /// or killed mid-run is an orphan: it is emptied at once, except a rejected code kept for reading (the folder goes with it after
    /// <see cref="WorkKeep"/>). Scene images that were paid for stay for a retry until <see cref="WorkKeep"/>.
    /// </summary>
    public void CleanWork()
    {
        var runs = Path.Combine(paths.WorkDirectory, "videos");
        if (Directory.Exists(runs))
        {
            foreach (var folder in Directory.EnumerateDirectories(runs))
            {
                if (clock.GetUtcNow() - Directory.GetLastWriteTimeUtc(folder) > WorkKeep)
                {
                    assembler.TryDelete(folder);
                }
                else
                {
                    assembler.CleanFailed(folder, new VersionTarget());
                }
            }
        }

        var images = Path.Combine(paths.WorkDirectory, "scene-images");
        if (Directory.Exists(images))
        {
            foreach (var folder in Directory.EnumerateDirectories(images).Where(f => clock.GetUtcNow() - Directory.GetLastWriteTimeUtc(f) > WorkKeep))
            {
                assembler.TryDelete(folder);
            }
        }
    }

    private async Task<List<string>> ImagesAsync(VideoJob job, ResolvedModel? imageModel, IReadOnlyList<Scene> scenes, string work, List<string> warnings,
        List<VideoCost> spent, Guid id, int width, int height, Action<double> report, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        var publicFolder = Path.Combine(work, "project", "public");
        void Place(string source, string name)
        {
            Directory.CreateDirectory(publicFolder);
            File.Copy(source, Path.Combine(publicFolder, name), overwrite: true);
            File.Copy(source, Path.Combine(work, "project", name), overwrite: true);
        }

        try
        {
            if (job.Options.Images == ImageSources.Samples)
            {
                var samples = await profiles.ImagesAsync(job.Profile.Id, cancellationToken).ConfigureAwait(false);
                if (samples.Count == 0)
                {
                    // 0.1.6 made the video without images and said nothing: the sample images were chosen, so say why there are none.
                    logger.LogWarning("The sample images were chosen but the profile has none; the video is made without images");
                    warnings.Add(VideoWarnings.SamplesNone);
                    return names;
                }

                var missing = 0;
                for (var i = 0; i < scenes.Count; i++)
                {
                    var sample = samples[i % samples.Count];
                    var name = "sample-" + (i % samples.Count + 1).ToString(CultureInfo.InvariantCulture) + Path.GetExtension(sample.FileName);

                    // The stored name is relative to the library folder ("<profile>/images/<file>"), like every library path.
                    var source = SampleFile(sample.FileName);
                    if (source is not null && File.Exists(source))
                    {
                        Place(source, name);
                        names.Add(name);
                    }
                    else
                    {
                        missing++;
                        names.Add(string.Empty);
                    }
                }

                if (missing > 0)
                {
                    logger.LogWarning("{Missing} scene(s) have no image: their sample image file is missing", missing);
                    warnings.Add(VideoWarnings.ImagesFailed);
                }
            }
            else if (imageModel is not null)
            {
                // The video code model writes the image prompts (one model less to choose).
                var promptModel = job.CodeModel;
                var engine = images.FirstOrDefault(e => e.Code == imageModel.Model.Provider)
                    ?? throw new VideoException($"{imageModel.Model.Provider} has no image engine.") { Code = "images" };
                var visuals = scenes.Select(s => s.Visual.Length > 0 ? s.Visual : s.Narration).ToList();

                // Prompts and images of this concept are kept until its video is made: a retry after a later failure reuses them.
                var cache = Path.Combine(SceneImageRoot(job.Concept.Id), CacheKey(job.Profile.Name, imageModel.Model, width, height, visuals));
                Directory.CreateDirectory(cache);
                var promptFile = Path.Combine(cache, "prompts.txt");
                IReadOnlyList<string> prompts;
                if (File.Exists(promptFile) && (await File.ReadAllLinesAsync(promptFile, cancellationToken).ConfigureAwait(false)) is { } kept && kept.Length == visuals.Count)
                {
                    prompts = kept;
                    logger.LogInformation("Scene image prompts reused from an earlier attempt");
                }
                else
                {
                    var chat = chats.Create(promptModel.Model, promptModel.Key.Secret);
                    ChatResponse answer;
                    try
                    {
                        answer = await chat.SendAsync(new ChatRequest(ImagePromptBuilder.System, ImagePromptBuilder.User(job.Profile.Name, visuals),
                            Schema: ImagePromptBuilder.Schema(), MaxOutputTokens: 4000, Effort: promptModel.Effort), cancellationToken).ConfigureAwait(false);
                    }
                    catch (Providers.ProviderException ex) when (ex.ChargedUsage is { } charged)
                    {
                        spent.Add(new VideoCost { VideoId = id, Step = PipelineSteps.ImagePromptCost, Usd = costs.Exact(promptModel.Model, charged), Exact = true });
                        throw;
                    }

                    spent.Add(new VideoCost
                    {
                        VideoId = id,
                        Step = PipelineSteps.ImagePromptCost,
                        Usd = costs.Exact(promptModel.Model, answer.Usage, answer.ReportedCostUsd),
                        Exact = true,
                    });
                    prompts = ImagePromptBuilder.Parse(answer.Text, visuals,
                        problem => logger.LogWarning("The image prompts could not all be read ({Problem}); scene descriptions are used for the rest", problem));
                    await File.WriteAllLinesAsync(promptFile, prompts.Select(p => p.ReplaceLineEndings(" ")), cancellationToken).ConfigureAwait(false);
                }

                for (var i = 0; i < prompts.Count; i++)
                {
                    var file = Path.Combine(cache, "scene-" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".png");
                    if (!File.Exists(file))
                    {
                        var result = await engine.GenerateAsync(new ImageRequest(prompts[i], width, height, file, imageModel), cancellationToken).ConfigureAwait(false);
                        logger.LogInformation("Scene image {Number} of {Count} made with {Provider}/{Model}", i + 1, prompts.Count, imageModel.Model.Provider,
                            imageModel.Model.Id);
                        spent.Add(new VideoCost { VideoId = id, Step = PipelineSteps.Images, Usd = result.CostUsd, Exact = result.CostUsd == 0 });
                    }

                    Place(file, Path.GetFileName(file));
                    names.Add(Path.GetFileName(file));
                    report((i + 1.0) / prompts.Count);
                }
            }
        }
        catch (Exception ex) when (ex is ModelNotReadyException or Providers.ProviderException or IOException or VideoException or InvalidOperationException)
        {
            logger.LogError(ex, "Scene images failed; the video is made without them");
            warnings.Add(VideoWarnings.ImagesFailed);
            names.Clear();
        }

        return names;
    }

    private string? SampleFile(string stored)
    {
        try
        {
            return LibraryPaths.Resolve(paths.LibraryDirectory, stored);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "A sample image path is outside the library folder and is ignored");
            return null;
        }
    }

    private string SceneImageRoot(Guid conceptId) => Path.Combine(paths.WorkDirectory, "scene-images", conceptId.ToString("N"));

    private static string CacheKey(string channel, CatalogModel model, int width, int height, IReadOnlyList<string> visuals)
    {
        var text = string.Join('\n', new[] { channel, model.Provider, model.Id, width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture) }
            .Concat(visuals));
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
