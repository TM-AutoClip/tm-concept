using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Images;
using TMConcept.Core.Markers;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Plans;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Videos;

/// <summary>
/// "Fix" (F16, 0.1.49): a new version of a made video from the notes left on it while it played (KURALLAR 12: a new version folder, the old one
/// is kept). All the notes go into ONE call of the video code model (<see cref="SceneFixPrompt"/>); only the noted scenes change: a scene whose
/// narration changed is voiced again (a local voice costs nothing; an online one is paid), a scene with a new image prompt gets a new picture
/// (paid with an online image model), and in a video drawn by the app a noted scene gets the new plan of the answer (cleaned and timed like
/// every plan). Every other scene keeps its clip, picture and plan; the kept code is rendered again with the new input. The scenes' timing,
/// subtitles, music and the version's record follow as for a new voice (<see cref="VideoRemaker.RevoiceAsync"/>).
/// </summary>
public sealed class SceneFixer(IChatClientFactory chats, ICostCalculator costs, SceneNarrator narrator, VideoAssembler assembler,
    IEnumerable<IImageEngine> images, IGpuCoordinator gpu, ILogger<SceneFixer> logger)
{
    private static readonly (string Step, double Weight)[] Weights =
        [(VideoSteps.Prepare, 2), (VideoSteps.Code, 10), (VideoSteps.Images, 10), (VideoSteps.Speech, 15), (VideoSteps.Timing, 1), (VideoSteps.Render, 50),
            (VideoSteps.Mux, 10), (VideoSteps.Save, 2)];

    /// <summary>Output tokens of the fix answer per noted scene (narration and image prompt; a plan needs the high end).</summary>
    public const long TokensPerScene = 300;

    /// <summary>Output tokens of a drawn scene's plan in the fix answer, at most.</summary>
    public const long PlanTokensPerScene = 1500;

    /// <summary>
    /// The estimate shown before "Fix": the one call (its real prompt; the answer from <see cref="TokensPerScene"/> per scene to the high end
    /// with plans), at the high end a new picture for every noted scene with a picture (online image model) and a new online voice for every
    /// noted scene's narration. Local models cost nothing.
    /// </summary>
    public CostEstimate Estimate(VersionSource source, IReadOnlyList<SceneNote> notes, CatalogModel code, CatalogModel? image, CatalogModel? voice)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(code);
        var plans = PlanDrawer.IsDrawer(source.Code);
        var noted = Noted(source.Input, notes);
        var prompt = costs.Tokens(SceneFixPrompt.System(plans) + SceneFixPrompt.User(source.Input, source.Texts, noted, plans), source.Input.Language);
        var call = costs.EstimateChat(code, prompt, 0, TokensPerScene * noted.Count, (TokensPerScene + (plans ? PlanTokensPerScene : 0)) * noted.Count);
        var pictures = image is { Provider: not ModelProviders.Local }
            ? image.ImageUsd * noted.Count(n => source.Input.Scenes[n.Scene - 1].Image.Length > 0)
            : 0;
        var speech = VideoCostEstimator.Speech(voice, noted.Sum(n => (long)MarkerText.Spoken(source.Texts.ElementAtOrDefault(n.Scene - 1)).Length));
        return call with { MaxUsd = call.MaxUsd + pictures + speech };
    }

    /// <summary>Makes the fixed version.</summary>
    public async Task<VideoRecord> FixAsync(FixJob job, IProgress<VideoProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var source = job.Source;
        var input = source.Input;
        var notes = Noted(input, job.Notes);
        if (notes.Count == 0)
        {
            throw new VideoException("There is no note on a scene of this version.") { Code = "fix" };
        }

        var status = source.Engine.Status();
        if (!status.Ready)
        {
            throw new VideoException(source.Engine.Name + " is not installed (missing: " + string.Join(", ", status.Missing) + ").") { Code = "missing" };
        }

        var id = Guid.NewGuid();
        var watch = Stopwatch.StartNew();
        var work = assembler.WorkFolder(id);
        var target = new VersionTarget();
        var steps = new VideoStepProgress(Weights, progress);
        var plans = PlanDrawer.IsDrawer(source.Code);
        logger.LogInformation("Fixed version {Id} started: {Count} noted scenes ({Scenes}), {Engine}, model {Provider}/{Model}", id, notes.Count,
            string.Join(", ", notes.Select(n => n.Scene)), source.Engine.Name, job.CodeModel.Model.Provider, job.CodeModel.Model.Id);
        return await assembler.GuardAsync(job.Profile.Id, job.CodeModel, work, target, steps, watch, async () =>
        {
            steps.Report(VideoSteps.Prepare, 0);
            var project = Path.Combine(work, "project");
            VideoAssembler.CopyProject(Path.Combine(source.Folder, VideoFolders.Source), project);
            var options = VideoRemaker.Options(source.Record);
            var music = await assembler.PrepareMusicAsync(options.Style, source.Folder, work, cancellationToken).ConfigureAwait(false);
            steps.Finish(VideoSteps.Prepare);

            // The one call for all the notes.
            steps.Report(VideoSteps.Code, 0.1);
            var answer = await AskAsync(job, input, source.Texts, notes, plans, steps, id, cancellationToken).ConfigureAwait(false);
            var fixes = SceneFixPrompt.Read(answer);
            var planItems = plans ? PlanReader.Read(answer).Where(p => notes.Any(n => n.Scene == p.Scene)).ToList() : [];
            steps.Finish(VideoSteps.Code);

            var texts = source.Texts.ToList();
            var changed = new HashSet<int>();
            foreach (var note in notes)
            {
                if (fixes.TryGetValue(note.Scene, out var fix) && fix.Narration.Length > 0 && fix.Narration != texts[note.Scene - 1])
                {
                    texts[note.Scene - 1] = fix.Narration;
                    changed.Add(note.Scene);
                }
            }

            await PicturesAsync(job, input, notes, fixes, project, steps, id, cancellationToken).ConfigureAwait(false);
            var clips = await ClipsAsync(job, input, texts, changed, options, work, steps, id, cancellationToken).ConfigureAwait(false);

            // Timing as for a new voice; the kept plans follow their scenes, the noted scenes of a drawn video take their new plans.
            steps.Report(VideoSteps.Timing, 0);
            var sources = input.Scenes.Select((s, i) =>
            {
                var script = texts[i];
                return new RenderSceneSource(s.Visual, MarkerText.Spoken(script), s.Image, clips[i]?.DurationMs ?? VideoRemaker.SilentMs(script, input.Language),
                    MarkerText.Transition(script));
            }).ToList();
            var timed = RenderInputBuilder.Build(input.Title, input.Language, sources, input.Width, input.Height, input.Fps, input.Theme,
                Styles.VideoStyleRules.EndHoldMs(options.Style, input.Language));
            var spans = clips.Select(c => c?.Spans).ToList();
            PlanTiming.Carry(input, timed, spans);
            if (plans)
            {
                NewPlans(timed, planItems, spans);
            }

            music = await assembler.Music.SettleAsync(music, options.Style, timed, clips, work, align: true, cancellationToken, VideoMusicStep.Spender(steps, id))
                .ConfigureAwait(false);
            steps.Finish(VideoSteps.Timing);
            var rendered = await assembler.RenderKeptAsync(source.Engine, source.Code, options, timed, work, steps, cancellationToken).ConfigureAwait(false);
            var warnings = source.Record.Warnings.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w != VideoWarnings.NoThumbnail && !RenderOutcome.Codes.Contains(w) && !VideoWarnings.Motion.Contains(w)
                    && !VideoWarnings.Subtitles.Contains(w) && !VideoWarnings.Quality.Contains(w)).ToList();
            var record = await assembler.SaveAsync(new VersionSave(id, job.Profile, source.Record.ConceptId, source.Record.Title, work, rendered, clips, target,
                VersionFolders.Next(Path.GetDirectoryName(source.Folder)!), options, source.Engine.Name, source.Record.CodeModel, source.Record.SpeechModel,
                source.Record.ImageModel, source.Record.PromptVersion, warnings, steps.Spent, [], watch) { Script = texts, Music = music }, steps,
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Fixed version {Id} made in {Seconds:0} s: {Texts} narrations, {Plans} plans changed ({Usd} USD)", id,
                watch.Elapsed.TotalSeconds, changed.Count, planItems.Count, record.TotalUsd);
            assembler.TryDelete(work);
            return record;
        }).ConfigureAwait(false);
    }

    /// <summary>The notes on scenes the version has (others are left out), in scene order.</summary>
    public static IReadOnlyList<SceneNote> Noted(RenderInput input, IReadOnlyList<SceneNote> notes)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(notes);
        return notes.Where(n => n.Scene >= 1 && n.Scene <= input.Scenes.Count && n.Text.Trim().Length > 0).OrderBy(n => n.Scene).ToList();
    }

    /// <summary>The one paid call (spent also when it is cut off or fails after being charged).</summary>
    private async Task<string> AskAsync(FixJob job, RenderInput input, IReadOnlyList<string> texts, IReadOnlyList<SceneNote> notes, bool plans,
        VideoStepProgress steps, Guid id, CancellationToken cancellationToken)
    {
        var model = job.CodeModel;
        var chat = chats.Create(model.Model, model.Key.Secret);
        var request = new ChatRequest(SceneFixPrompt.System(plans), SceneFixPrompt.User(input, texts, notes, plans), Schema: SceneFixPrompt.Schema(plans),
            MaxOutputTokens: 16000, Effort: model.Effort);
        try
        {
            var answer = await chat.SendAsync(request, cancellationToken).ConfigureAwait(false);
            Spend(steps, id, model, answer.Usage, answer.ReportedCostUsd);
            return answer.Text;
        }
        catch (Providers.ProviderException ex) when (ex.Code is "truncated" or "incomplete")
        {
            logger.LogWarning("The fix answer was cut off ({Code}); its complete scenes are used", ex.Code);
            if (ex.ChargedUsage is { } charged)
            {
                Spend(steps, id, model, charged, null);
            }

            return ex.PartialText;
        }
        catch (Providers.ProviderException ex) when (ex.ChargedUsage is { } charged)
        {
            Spend(steps, id, model, charged, null);
            throw;
        }
    }

    /// <summary>New pictures for the noted scenes the answer gave an image prompt (only with an image model and a scene that has a picture).</summary>
    private async Task PicturesAsync(FixJob job, RenderInput input, IReadOnlyList<SceneNote> notes, IReadOnlyDictionary<int, (string Narration, string Image)> fixes,
        string project, VideoStepProgress steps, Guid id, CancellationToken cancellationToken)
    {
        var wanted = notes.Where(n => fixes.TryGetValue(n.Scene, out var f) && f.Image.Length > 0 && input.Scenes[n.Scene - 1].Image.Length > 0).ToList();
        if (wanted.Count == 0 || job.ImageModel is not { } model)
        {
            if (wanted.Count > 0)
            {
                logger.LogInformation("{Count} new pictures were asked for, but no image model is chosen; the pictures are kept", wanted.Count);
            }

            steps.Finish(VideoSteps.Images);
            return;
        }

        var engine = images.FirstOrDefault(e => e.Code == model.Model.Provider)
            ?? throw new VideoException(model.Model.Provider + " has no image engine.") { Code = "images" };
        try
        {
            steps.Report(VideoSteps.Images, 0, done: 0, total: wanted.Count);
            var lease = await gpu.AcquireAsync(model.Model, cancellationToken).ConfigureAwait(false);
            await using var leaseScope = lease.ConfigureAwait(false);
            for (var i = 0; i < wanted.Count; i++)
            {
                var scene = input.Scenes[wanted[i].Scene - 1];
                var file = Path.Combine(project, "fix-" + scene.Number.ToString(CultureInfo.InvariantCulture) + ".png");
                var result = await engine.GenerateAsync(new ImageRequest(fixes[scene.Number].Image, input.Width, input.Height, file, model), cancellationToken)
                    .ConfigureAwait(false);
                steps.Spend(new VideoCost { VideoId = id, Step = PipelineSteps.Images, Usd = result.CostUsd, Exact = result.CostUsd == 0 });
                if (!File.Exists(file))
                {
                    throw new VideoException(string.Create(CultureInfo.InvariantCulture, $"{model.Model.Name} made no image file for scene {scene.Number}."))
                    {
                        Code = "images",
                    };
                }

                // Under the scene's own name, where the kept code looks for it (the project's root and its public/ folder).
                File.Copy(file, Path.Combine(project, scene.Image), overwrite: true);
                var publicFolder = Path.Combine(project, "public");
                Directory.CreateDirectory(publicFolder);
                File.Copy(file, Path.Combine(publicFolder, scene.Image), overwrite: true);
                File.Delete(file);
                steps.Report(VideoSteps.Images, (i + 1.0) / wanted.Count, done: i + 1, total: wanted.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not Providers.ProviderException and not VideoException)
        {
            logger.LogError(LogEvents.OnScreen, ex, "A new scene picture failed; the fixed version is stopped");
            throw new VideoException(ex.Message, ex) { Code = ex is MemoryShortfallException ? "memory" : "images" };
        }

        steps.Finish(VideoSteps.Images);
    }

    /// <summary>The clips: a changed narration voiced again (or silent without a voice), every other scene its kept clip.</summary>
    private async Task<List<SpeechClip?>> ClipsAsync(FixJob job, RenderInput input, IReadOnlyList<string> texts, IReadOnlySet<int> changed, VideoOptions options,
        string work, VideoStepProgress steps, Guid id, CancellationToken cancellationToken)
    {
        var clips = new List<SpeechClip?>();
        var pacing = Styles.VideoStyleRules.Pacing(options.Style);
        var done = 0;
        foreach (var scene in input.Scenes)
        {
            if (changed.Contains(scene.Number))
            {
                clips.Add(job.Voice is null ? null : await VoiceAsync(job.Voice, input.Language, scene.Number, texts[scene.Number - 1], work, pacing, steps, id,
                    cancellationToken).ConfigureAwait(false));
                done++;
                steps.Report(VideoSteps.Speech, done / (double)changed.Count, done: done, total: changed.Count);
                continue;
            }

            var kept = VersionFolders.Clip(job.Source.Folder, scene.Number);
            clips.Add(File.Exists(kept) ? new SpeechClip(kept, WaveFile.DurationMs(kept), 0, 0, "kept") : null);
        }

        steps.Finish(VideoSteps.Speech);
        return clips;
    }

    /// <summary>One changed scene's new clip; what an online voice charged is spent, also when it fails.</summary>
    private async Task<SpeechClip?> VoiceAsync(SpeechSetup voice, string language, int number, string script, string work, NarrationPacing pacing,
        VideoStepProgress steps, Guid id, CancellationToken cancellationToken)
    {
        decimal cost = 0;
        try
        {
            return await narrator.VoiceAsync(voice, language, number, script, work, pacing, usd => cost += usd, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SpeechException or InvalidDataException)
        {
            throw new VideoException("The voice could not be made: " + ex.Message, ex) { Code = "speech" };
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
    /// The noted scenes of a drawn video take the answer's plans: cleaned like every plan, the kept motion, timed from their sentences. Also the
    /// careful quality mode's repaired scenes (0.1.50, <see cref="QualityStep"/>).
    /// </summary>
    public static void NewPlans(RenderInput timed, IReadOnlyList<PlanItem> items, IReadOnlyList<IReadOnlyList<SpeechSpan>?> spans)
    {
        if (items.Count == 0)
        {
            return;
        }

        var sentences = timed.Scenes.Select((s, i) => PlanTiming.Sentences(s.Caption, spans.ElementAtOrDefault(i)).Count).ToList();
        var cleaned = PlanReader.Clean(items, sentences, timed.Scenes.Select(s => s.Image.Length > 0).ToList());
        foreach (var item in items)
        {
            var i = item.Scene - 1;
            if (i < 0 || i >= timed.Scenes.Count)
            {
                continue;
            }

            var scene = timed.Scenes[i];
            var plan = cleaned[i].Plan;
            if (scene.Plan is { } kept)
            {
                // How the scene comes in and drifts stays as it was (the film's motion is not the note's subject).
                plan.Transition = kept.Transition;
                plan.TransitionSeconds = kept.TransitionSeconds;
                plan.Drift = kept.Drift;
                plan.Music = kept.Music;
            }

            var sceneSpans = spans.ElementAtOrDefault(i);
            PlanTiming.Apply(plan, scene.StartSeconds, scene.DurationSeconds,
                PlanTiming.Starts(PlanTiming.Sentences(scene.Caption, sceneSpans), sceneSpans, scene.DurationSeconds));
            scene.Plan = plan;
        }
    }

    private void Spend(VideoStepProgress steps, Guid id, ResolvedModel model, ChatUsage usage, decimal? reported) =>
        steps.Spend(new VideoCost { VideoId = id, Step = PipelineSteps.VideoCode, Usd = costs.Exact(model.Model, usage, reported), Exact = true });
}
