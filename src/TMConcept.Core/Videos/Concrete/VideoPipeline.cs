using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Engines;
using TMConcept.Core.Images;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Scenes;
using TMConcept.Core.Speech;
using TMConcept.Core.Subtitles;

namespace TMConcept.Core.Videos;

/// <summary>
/// Concept → video (plan §6.1): A prepare (scenes, no model) · B narration per scene (cached; off or failed = silent video with a warning)
/// · C timing in C# · D images (samples or an image model) · E video code from the model, safety check, one repair · F render (processor
/// only), one repair with the error text · H mux, subtitles (.srt always, burned ASS when chosen), narration text file · I thumbnail,
/// files into the library folder, video and cost rows, concept marked produced.
/// </summary>
public sealed class VideoPipeline(IEnumerable<IVideoEngine> engines, SpeechService speech, IEnumerable<IImageEngine> images, StepModelResolver resolver,
    IChatClientFactory chats, ICostCalculator costs, IMediaTools media, IVideoStore videos, IConceptStore concepts, IProfileStore profiles, IAppPaths paths,
    TimeProvider clock, ILogger<VideoPipeline> logger)
{
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
        var id = Guid.NewGuid();
        var work = Path.Combine(paths.WorkDirectory, "videos", id.ToString("N"));
        var warnings = new List<string>();
        var spent = new List<VideoCost>();
        var speechOn = options.Speech && profile.SpeechEngine != SpeechEngines.Off;
        var active = Weights.Where(w => (w.Step != "speech" || speechOn) && (w.Step != "images" || options.Images != ImageSources.None)).ToList();
        var total = active.Sum(w => w.Weight);
        var done = 0.0;
        void Report(string step, double inside, string detail = "")
        {
            var weight = active.FirstOrDefault(w => w.Step == step).Weight;
            progress?.Report(new VideoProgress(Math.Clamp((done + (weight * inside)) / total, 0, 1), step, detail));
        }

        void Finish(string step)
        {
            done += active.FirstOrDefault(w => w.Step == step).Weight;
            Report(step, 0);
        }

        try
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
                warnings.Add(VideoWarnings.SpeechOff);
                logger.LogWarning("Narration is off; the video will be silent");
            }
            else
            {
                try
                {
                    for (var i = 0; i < split.Scenes.Count; i++)
                    {
                        var narration = split.Scenes[i].Narration;
                        clips.Add(narration.Length == 0 ? null : await speech.SynthesizeAsync(profile.SpeechEngine, profile.SpeechVoice, profile.SpeechSpeed,
                            profile.ContentLanguage, narration, Path.Combine(work, "audio", (i + 1).ToString(CultureInfo.InvariantCulture) + ".wav"), cancellationToken)
                            .ConfigureAwait(false));
                        Report("speech", (i + 1.0) / split.Scenes.Count);
                    }

                    var speechCost = clips.Sum(c => c?.CostUsd ?? 0);
                    if (speechCost > 0)
                    {
                        spent.Add(new VideoCost { VideoId = id, Step = PipelineSteps.Speech, Usd = speechCost, Exact = false });
                    }

                    warnings.Add(profile.SpeechEngine == SpeechEngines.PiperTurkish ? VideoWarnings.NonCommercialVoice : VideoWarnings.AiVoice);
                }
                catch (Exception ex) when (ex is SpeechException or Providers.ProviderException or IOException or InvalidDataException)
                {
                    logger.LogError(ex, "Narration failed; the video will be silent");
                    warnings.Add(VideoWarnings.SpeechFailed);
                    clips.Clear();
                }

                Finish("speech");
            }

            // C. timing
            var (width, height) = RenderInputBuilder.Size(profile.Format);
            var sceneImages = await ImagesAsync(job, split.Scenes, work, warnings, spent, id, width, height, p => Report("images", p), cancellationToken)
                .ConfigureAwait(false);
            var sources = split.Scenes.Select((s, i) => new RenderSceneSource(s.Visual, s.Narration, sceneImages.ElementAtOrDefault(i) ?? string.Empty,
                clips.ElementAtOrDefault(i)?.DurationMs ?? EstimateMs(s.Narration, profile))).ToList();
            var input = RenderInputBuilder.Build(job.Concept.Title, profile.ContentLanguage, sources, width, height, 30);
            Finish("timing");
            if (options.Images != ImageSources.None)
            {
                Finish("images");
            }

            // E. code (+ one repair after a rejected check)
            var chat = chats.Create(job.CodeModel.Model, job.CodeModel.Key.Secret);
            var repaired = false;
            Report("code", 0.1);
            var code = await CodeAsync(chat, job, engine, VideoCodePrompt.User(input), spent, id, cancellationToken).ConfigureAwait(false);
            var silent = Path.Combine(work, "silent.mp4");
            var request = new EngineRenderRequest(Path.Combine(work, "project"), code, input, silent, Workers());
            try
            {
                engine.WriteProject(request);
            }
            catch (EngineException ex) when (ex.Code == "check")
            {
                File.WriteAllText(Path.Combine(work, "rejected-1.txt"), code);
                logger.LogWarning("The video code was rejected; asking for one repair: {Findings}", string.Join(" · ", ex.Findings.Take(4)));
                code = await CodeAsync(chat, job, engine, VideoCodePrompt.Repair(code, string.Join("\n", ex.Findings)), spent, id, cancellationToken)
                    .ConfigureAwait(false);
                request = request with { Code = code };
                repaired = true;
                try
                {
                    engine.WriteProject(request);
                }
                catch (EngineException again) when (again.Code == "check")
                {
                    File.WriteAllText(Path.Combine(work, "rejected-2.txt"), code);
                    throw new VideoException("The video code did not pass the safety check twice and was not run: " + string.Join(" · ", again.Findings.Take(4)))
                    {
                        Code = "code",
                    };
                }
            }

            Finish("code");

            // F. render (+ one repair with the error text)
            var engineProgress = new Progress<EngineEvent>(e => Report("render", e.Ratio ?? 0, e.Message));
            try
            {
                await engine.RenderAsync(request, engineProgress, cancellationToken).ConfigureAwait(false);
            }
            catch (EngineException ex) when (ex.Code == "render" && !repaired)
            {
                logger.LogWarning("Render failed; asking for one repair: {Error}", ex.Message);
                code = await CodeAsync(chat, job, engine, VideoCodePrompt.Repair(code, ex.Message), spent, id, cancellationToken).ConfigureAwait(false);
                request = request with { Code = code };
                repaired = true;
                try
                {
                    engine.WriteProject(request);
                    await engine.RenderAsync(request, engineProgress, cancellationToken).ConfigureAwait(false);
                }
                catch (EngineException again)
                {
                    throw new VideoException("Render failed after the repair: " + again.Message, again) { Code = again.Code == "check" ? "code" : "render" };
                }
            }
            catch (EngineException ex)
            {
                throw new VideoException("Render failed: " + ex.Message, ex) { Code = ex.Code == "check" ? "code" : "render" };
            }

            if (repaired)
            {
                warnings.Add(VideoWarnings.Repaired);
            }

            Finish("render");

            // H. mux, subtitles, narration file
            var folder = LibraryFolder(profile, job.Concept);
            Directory.CreateDirectory(folder);
            var cueSources = input.Scenes.Select(s => new SubtitleSource((long)Math.Round(s.StartSeconds * 1000),
                clips.ElementAtOrDefault(s.Number - 1)?.DurationMs ?? (long)Math.Round(s.DurationSeconds * 1000), s.Caption)).ToList();
            var cues = SubtitleBuilder.Build(cueSources);
            await File.WriteAllTextAsync(Path.Combine(folder, "subtitles.srt"), SubtitleBuilder.Srt(cues), cancellationToken).ConfigureAwait(false);
            string? ass = null;
            if (options.Subtitles && cues.Count > 0)
            {
                ass = "subtitles.ass";
                await File.WriteAllTextAsync(Path.Combine(work, ass), SubtitleBuilder.Ass(cues, width, height, input.Theme), cancellationToken).ConfigureAwait(false);
            }

            if (options.NarrationFile)
            {
                await File.WriteAllTextAsync(Path.Combine(folder, "narration.txt"), NarrationScript.Text(job.Concept.Title, profile.ContentLanguage, cueSources),
                    cancellationToken).ConfigureAwait(false);
            }

            var audio = clips.Select((c, i) => (Clip: c, Start: (long)Math.Round(input.Scenes[i].StartSeconds * 1000))).Where(x => x.Clip is not null)
                .Select(x => (x.Clip!.File, x.Start)).ToList();
            var output = Path.Combine(folder, "video.mp4");
            Report("mux", 0.1);
            try
            {
                await media.MuxAsync(work, silent, audio, ass, output, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                throw new VideoException("Joining video and narration failed: " + ex.Message, ex) { Code = "mux" };
            }

            Finish("mux");

            // I. thumbnail and records
            var thumbnail = await media.ThumbnailAsync(output, Path.Combine(folder, "thumbnail.jpg"), cancellationToken).ConfigureAwait(false);
            var record = new VideoRecord
            {
                Id = id,
                ConceptId = job.Concept.Id,
                ProfileId = profile.Id,
                Title = job.Concept.Title,
                Folder = LibraryPaths.Relative(paths.LibraryDirectory, folder),
                FileName = "video.mp4",
                ThumbnailName = thumbnail ? "thumbnail.jpg" : string.Empty,
                DurationMs = (long)Math.Round(input.DurationSeconds * 1000),
                Width = width,
                Height = height,
                Fps = input.Fps,
                SizeBytes = new FileInfo(output).Length,
                Engine = engine.Name,
                CodeModel = job.CodeModel.Model.Provider + "/" + job.CodeModel.Model.Id,
                SpeechModel = speechOn && !warnings.Contains(VideoWarnings.SpeechFailed) ? profile.SpeechEngine + ":" + profile.SpeechVoice : string.Empty,
                ImageModel = options.Images,
                SettingsJson = JsonSerializer.Serialize(options, Render.CoreJsonContext.Default.VideoOptions),
                PromptVersion = "1",
                TotalUsd = spent.Sum(c => c.Usd),
                CostExact = spent.All(c => c.Exact),
                ElapsedMs = watch.ElapsedMilliseconds,
                Warnings = string.Join(',', warnings.Distinct()),
                CreatedAtUtc = clock.GetUtcNow(),
            };
            await videos.AddAsync(record, spent, CancellationToken.None).ConfigureAwait(false);
            await concepts.SetStatusAsync([job.Concept.Id], ConceptStatus.Produced, CancellationToken.None).ConfigureAwait(false);
            Finish("save");
            logger.LogInformation("Video {Id} made in {Seconds:0} s ({Engine}, {Usd} USD)", id, watch.Elapsed.TotalSeconds, engine.Name, record.TotalUsd);
            TryDelete(work);
            return record;
        }
        catch (VideoException ex)
        {
            logger.LogError("Video failed ({Code}): {Message}", ex.Code, ex.Message);
            throw new VideoException(ex.Message, ex) { Code = ex.Code, SpentUsd = spent.Sum(c => c.Usd) };
        }
        catch (Providers.ProviderException ex)
        {
            logger.LogError("Video failed: provider error ({Code})", ex.Code);
            throw new VideoException(ex.Message, ex) { Code = "provider:" + ex.Code, SpentUsd = spent.Sum(c => c.Usd) };
        }
    }

    /// <summary>Render workers: cores − 2 (at least 1).</summary>
    public static int Workers() => Math.Clamp(Environment.ProcessorCount - 2, 1, 8);

    private async Task<string> CodeAsync(IChatClient chat, VideoJob job, IVideoEngine engine, string user, List<VideoCost> spent, Guid id,
        CancellationToken cancellationToken)
    {
        var answer = await chat.SendAsync(new ChatRequest(VideoCodePrompt.System(engine), user, CacheSystem: true, MaxOutputTokens: 32_000,
            Effort: job.CodeModel.Effort), cancellationToken).ConfigureAwait(false);
        spent.Add(new VideoCost
        {
            VideoId = id,
            Step = PipelineSteps.VideoCode,
            Usd = costs.Exact(job.CodeModel.Model, answer.Usage, answer.ReportedCostUsd),
            Exact = true,
        });
        return EngineCodeCheck.ExtractCode(answer.Text);
    }

    private async Task<List<string>> ImagesAsync(VideoJob job, IReadOnlyList<Scene> scenes, string work, List<string> warnings, List<VideoCost> spent, Guid id,
        int width, int height, Action<double> report, CancellationToken cancellationToken)
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
                var folder = Path.Combine(paths.LibraryDirectory, LibraryPaths.SafeName(job.Profile.Name), "images");
                for (var i = 0; i < scenes.Count && samples.Count > 0; i++)
                {
                    var sample = samples[i % samples.Count];
                    var name = "sample-" + (i % samples.Count + 1).ToString(CultureInfo.InvariantCulture) + Path.GetExtension(sample.FileName);
                    var source = Path.Combine(folder, sample.FileName);
                    if (File.Exists(source))
                    {
                        Place(source, name);
                        names.Add(name);
                    }
                    else
                    {
                        names.Add(string.Empty);
                    }
                }
            }
            else if (job.Options.Images == ImageSources.Model)
            {
                var promptModel = await resolver.ResolveAsync(PipelineSteps.ImagePrompt, null, cancellationToken).ConfigureAwait(false);
                var imageModel = await resolver.ResolveAsync(PipelineSteps.Image, null, cancellationToken).ConfigureAwait(false);
                var engine = images.FirstOrDefault(e => e.Code == imageModel.Model.Provider)
                    ?? throw new VideoException($"{imageModel.Model.Provider} has no image engine.") { Code = "images" };
                var visuals = scenes.Select(s => s.Visual.Length > 0 ? s.Visual : s.Narration).ToList();
                var chat = chats.Create(promptModel.Model, promptModel.Key.Secret);
                var answer = await chat.SendAsync(new ChatRequest(ImagePromptBuilder.System, ImagePromptBuilder.User(job.Profile.Name, visuals),
                    Schema: ImagePromptBuilder.Schema(), MaxOutputTokens: 4000, Effort: promptModel.Effort), cancellationToken).ConfigureAwait(false);
                spent.Add(new VideoCost
                {
                    VideoId = id,
                    Step = PipelineSteps.ImagePrompt,
                    Usd = costs.Exact(promptModel.Model, answer.Usage, answer.ReportedCostUsd),
                    Exact = true,
                });
                var prompts = ImagePromptBuilder.Parse(answer.Text, visuals);
                for (var i = 0; i < prompts.Count; i++)
                {
                    var file = Path.Combine(work, "images", "scene-" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".png");
                    var result = await engine.GenerateAsync(new ImageRequest(prompts[i], width, height, file, imageModel), cancellationToken).ConfigureAwait(false);
                    spent.Add(new VideoCost { VideoId = id, Step = PipelineSteps.Image, Usd = result.CostUsd, Exact = false });
                    Place(file, Path.GetFileName(file));
                    names.Add(Path.GetFileName(file));
                    report((i + 1.0) / prompts.Count);
                }
            }
        }
        catch (Exception ex) when (ex is ModelNotReadyException or Providers.ProviderException or IOException or VideoException)
        {
            logger.LogError(ex, "Scene images failed; the video is made without them");
            warnings.Add(VideoWarnings.ImagesFailed);
            names.Clear();
        }

        return names;
    }

    private static long EstimateMs(string narration, Profile profile)
    {
        var words = narration.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var rate = profile.ContentLanguage == ContentLanguages.Turkish ? NarrationRate.TurkishWordsPerSecond : NarrationRate.EnglishWordsPerSecond;
        return (long)Math.Round(words / rate * 1000);
    }

    private string LibraryFolder(Profile profile, Concept concept)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"{clock.GetLocalNow():yyyy-MM-dd}-{concept.Number}-{LibraryPaths.SafeName(concept.Title)}");
        var folder = Path.Combine(paths.LibraryDirectory, LibraryPaths.SafeName(profile.Name), name);
        for (var i = 2; Directory.Exists(folder) && File.Exists(Path.Combine(folder, "video.mp4")); i++)
        {
            folder = Path.Combine(paths.LibraryDirectory, LibraryPaths.SafeName(profile.Name), name + "-" + i.ToString(CultureInfo.InvariantCulture));
        }

        return folder;
    }

    private void TryDelete(string work)
    {
        try
        {
            Directory.Delete(work, recursive: true);
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "The work folder could not be removed yet");
        }
    }
}
