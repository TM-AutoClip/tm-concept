using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Engines;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Render;
using TMConcept.Core.Runs;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Core.Subtitles;

namespace TMConcept.Core.Videos;

/// <summary>
/// The steps every video run shares — the first make (<see cref="VideoPipeline"/>) and each later version (<see cref="VideoRemaker"/>): the
/// video code from the model with its safety check and one repair, the render with one repair; a render of kept code without any model;
/// the mux and the version folder with its record; and after a failure the clean-up and keeping what was paid as a run. Moved here from
/// the pipeline unchanged, so a version is made exactly as the first video was.
/// </summary>
public sealed class VideoAssembler(IChatClientFactory chats, ICostCalculator costs, IMediaTools media, IVideoStore videos, IRunStore runs, IAppPaths paths,
    TimeProvider clock, Plans.ScenePlanner planner, IMotionFrames motion, VideoMusicStep musicStep, SubtitleTimingStep subtitleTiming,
    ILogger<VideoAssembler> logger)
{
    /// <summary>The background music step (0.1.44 C3, 0.1.45 C4 + F7 + F8).</summary>
    public VideoMusicStep Music => musicStep;

    /// <summary>The start of a rejected code's file name in a work folder (kept for reading until the start-up clean-up).</summary>
    public const string Rejected = "rejected-";

    /// <summary>The work folder of a run.</summary>
    public string WorkFolder(Guid id) => Path.Combine(paths.WorkDirectory, "videos", id.ToString("N"));

    /// <summary>Render workers: cores − 2 (at least 1).</summary>
    public static int Workers() => Math.Clamp(Environment.ProcessorCount - 2, 1, 8);

    /// <summary>The effects of a finishing layer that are on (for the log).</summary>
    private static IEnumerable<string> FinishNames(RenderFinish finish) =>
        new StyleFinish(finish.Tooth, finish.Grain, finish.Vignette, finish.Bloom, finish.Shadow, finish.Boil).Effects;

    /// <summary>
    /// Takes the finishing layer (F12, 0.1.43) out of an input whose scenes are drawn by code a model wrote: only the app's drawer
    /// (<c>plan.js</c>) draws it, the model's code would only see an unknown field.
    /// </summary>
    private static void NoFinish(RenderInput input)
    {
        if (input.Style is { } style)
        {
            style.Finish = null;
        }
    }

    /// <summary>
    /// E + F: the video code from <paramref name="codeModel"/> for <paramref name="input"/> (paid; every call is spent in
    /// <paramref name="steps"/>, also a cut-off one; the call's phase and tokens are reported while it streams), written into
    /// <c>&lt;work&gt;/project</c> after the safety check (a rejected code gets one repair), then the render (a failed render gets one repair with
    /// its error text, unless the code was repaired already). Without subtitles (<see cref="VideoStyleRules.SubtitlesOn"/>) the prompt and the
    /// engine get the input without captions (<see cref="RenderInputBuilder.ForEngine"/>), so the code cannot draw the narration. With a
    /// template (<see cref="VideoOptions.Style"/>) the input gets its tokens (<see cref="RenderStyleBuilder"/>), the prompt its block, the project
    /// its font, and the code the template check after the safety check (<see cref="VideoStyleCheck"/>: a finding takes the same single repair;
    /// colours outside the palette are replaced without one). With scene images the code must show them (<see cref="ImageUseCheck"/>, a finding
    /// like the others). 20 Nov 2026: a model on this computer gets no repair after a failed check: the app's own animation is rendered at
    /// once (<see cref="IVideoEngine.FallbackCode"/>, until then only after a second failure). After the render one frame from the middle of
    /// every scene is checked (<see cref="FrameCheck"/>): a near-empty frame, or one that does not show its scene image, makes a local model's
    /// video render again with the app's own animation (<see cref="RenderOutcome.FrameFallback"/>). An online model's video stops with the
    /// reason ("frames"; no paid repair, the developer's decision) only when most of its scenes fail (<see cref="FrameCheckLimits.StopShare"/>;
    /// the 0.1.30 review: one title card or one split layout must not throw away a paid video while the limits are not tuned on real online
    /// renders); fewer failing scenes keep the video with a warning (<see cref="RenderOutcome.FrameWeak"/>), as does the app's own animation
    /// when it fails the check. A local model's code that fails to render gets its free repair, and when that fails too the app's own animation
    /// is rendered (the decision that a local model falls back to it). 0.1.33: a HyperFrames video whose template chooses the app's drawer
    /// (<see cref="Plans.PlanDrawer.Draws"/>) gets a scene plan from the same model instead of code (<see cref="DrawAsync"/>).
    /// </summary>
    public async Task<RenderOutcome> CodeAndRenderAsync(IVideoEngine engine, ResolvedModel codeModel, VideoOptions options, RenderInput input, string work,
        Guid id, VideoStepProgress steps, CancellationToken cancellationToken, PlanContext? plan = null)
    {
        if (Plans.PlanDrawer.Draws(engine.Kind, options?.Style))
        {
            return await DrawAsync(engine, codeModel, options!, input, work, id, steps, plan ?? new PlanContext(), cancellationToken).ConfigureAwait(false);
        }

        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(codeModel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(steps);
        var subtitles = VideoStyleRules.SubtitlesOn(options);
        var style = options.Style;
        RenderStyleBuilder.Apply(input, style);
        NoFinish(input);
        await BoostAsync(input, work, cancellationToken).ConfigureAwait(false);
        var chat = chats.Create(codeModel.Model, codeModel.Key.Secret);
        var local = IsLocal(codeModel.Model);
        var system = VideoCodePrompt.System(engine, subtitles, style, input.Language, local);
        var repaired = false;
        var appCode = false;
        steps.Report(VideoSteps.Code, 0.1);
        var engineInput = RenderInputBuilder.ForEngine(input, subtitles);
        PlaceFonts(Path.Combine(work, "project"), style, input.Language);
        var code = await CodeAsync(chat, codeModel, system, VideoCodePrompt.User(engineInput), steps, id, cancellationToken).ConfigureAwait(false);
        var request = new EngineRenderRequest(Path.Combine(work, "project"), code, engineInput, Path.Combine(work, "silent.mp4"), Workers());
        try
        {
            request = Write(engine, request, style);
        }
        catch (EngineException ex) when (ex.Code == "check" && local)
        {
            // A free model on this computer: its repair copied the example rather than fixing the design (19 Nov 2026), so the app's own
            // simple animation is used at once (the same checks, nothing is weakened).
            await File.WriteAllTextAsync(Path.Combine(work, Rejected + "1.txt"), code, cancellationToken).ConfigureAwait(false);
            request = AppCode(engine, request, style) ?? throw new VideoException("The video code did not pass the check and the app's own animation "
                + "did not either: " + string.Join(" · ", ex.Findings.Take(4))) { Code = "code" };
            logger.LogWarning("The local model's video code failed the check ({Findings}); the app's own animation is used",
                string.Join(" · ", ex.Findings.Take(4)));
            appCode = true;
        }
        catch (EngineException ex) when (ex.Code == "check")
        {
            await File.WriteAllTextAsync(Path.Combine(work, Rejected + "1.txt"), code, cancellationToken).ConfigureAwait(false);
            logger.LogWarning("The video code was rejected; asking for one repair: {Findings}", string.Join(" · ", ex.Findings.Take(4)));
            code = await CodeAsync(chat, codeModel, system, VideoCodePrompt.Repair(engine, code, ex.Findings), steps, id, cancellationToken)
                .ConfigureAwait(false);
            request = request with { Code = code };
            repaired = true;
            try
            {
                request = Write(engine, request, style);
            }
            catch (EngineException again) when (again.Code == "check")
            {
                await File.WriteAllTextAsync(Path.Combine(work, Rejected + "2.txt"), code, cancellationToken).ConfigureAwait(false);

                // Named after what is left: the first answer may have broken the safety rules only, and the repair the template only.
                var what = again.Message.StartsWith(TemplateRejection, StringComparison.Ordinal)
                    ? "The video code still did not follow the template after the repair and was not run: "
                    : again.Message.StartsWith(ImageRejection, StringComparison.Ordinal)
                        ? "The video code still did not show the scene images after the repair and was not run: "
                        : "The video code still did not pass the safety check after the repair and was not run: ";
                throw new VideoException(what + string.Join(" · ", again.Findings.Take(4))) { Code = "code" };
            }
        }

        steps.Finish(VideoSteps.Code);

        // F. render (+ one repair with the error text)
        var engineProgress = new ForwardProgress<EngineEvent>(e => steps.Report(VideoSteps.Render, e.Ratio ?? 0, e.Message));
        try
        {
            await engine.RenderAsync(request, engineProgress, cancellationToken).ConfigureAwait(false);
        }
        catch (EngineException ex) when (ex.Code == "render" && appCode)
        {
            // The app's own code is not sent to the model for a repair.
            throw new VideoException("Render failed: " + ex.Message, ex) { Code = "render" };
        }
        catch (EngineException ex) when (ex.Code == "render" && !repaired)
        {
            logger.LogWarning("Render failed; asking for one repair: {Error}", ex.Message);
            code = await CodeAsync(chat, codeModel, system, VideoCodePrompt.Repair(code, ex.Message), steps, id, cancellationToken).ConfigureAwait(false);
            request = request with { Code = code };
            repaired = true;
            try
            {
                request = Write(engine, request, style);
                await engine.RenderAsync(request, engineProgress, cancellationToken).ConfigureAwait(false);
            }
            catch (EngineException again) when (local)
            {
                // A free model on this computer: its repair did not pass the check or did not render either, so the app's own animation is used.
                logger.LogWarning("The local model's repaired video code failed too ({Error}); the app's own animation is used", again.Message);
                await File.WriteAllTextAsync(Path.Combine(work, Rejected + "2.txt"), code, cancellationToken).ConfigureAwait(false);
                request = AppCode(engine, request, style) ?? throw new VideoException("Render failed after the repair: " + again.Message, again)
                {
                    Code = again.Code == "check" ? "code" : "render",
                };
                appCode = true;
                await RenderAppCodeAsync(engine, request, engineProgress, cancellationToken).ConfigureAwait(false);
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

        // The frame check: what the video really shows.
        var frameFallback = false;
        var frameWeak = false;
        var (findings, seen) = await FrameFindingsAsync(request, cancellationToken).ConfigureAwait(false);
        if (findings.Count > 0 && appCode)
        {
            // The app's own animation is what it is: kept, with the warning.
            logger.LogWarning("The app's own animation does not pass the frame check either ({Findings}); it is kept", string.Join(" · ", findings));
            frameWeak = true;
        }
        else if (findings.Count > 0 && local)
        {
            logger.LogWarning("The local model's video does not show its scenes ({Findings}); it is rendered again with the app's own animation",
                string.Join(" · ", findings));
            request = AppCode(engine, request, style) ?? throw new VideoException("The rendered video does not show its scenes: " + string.Join("; ", findings))
            {
                Code = "frames",
            };
            await RenderAppCodeAsync(engine, request, engineProgress, cancellationToken).ConfigureAwait(false);
            appCode = true;
            frameFallback = true;
        }
        else if (findings.Count > seen * FrameCheckLimits.StopShare)
        {
            logger.LogError(Activity.LogEvents.OnScreen, "The rendered video does not show {Failed} of its {Seen} scenes ({Findings}); the video is stopped",
                findings.Count, seen, string.Join(" · ", findings));
            throw new VideoException("The rendered video does not show its scenes: " + string.Join("; ", findings)) { Code = "frames" };
        }
        else if (findings.Count > 0)
        {
            logger.LogWarning(Activity.LogEvents.OnScreen, "The rendered video does not show {Failed} of its {Seen} scenes ({Findings}); it is kept with a warning",
                findings.Count, seen, string.Join(" · ", findings));
            frameWeak = true;
        }

        steps.Finish(VideoSteps.Render);
        return new RenderOutcome(request, repaired, input) { AppCode = appCode, FrameFallback = frameFallback, FrameWeak = frameWeak };
    }

    /// <summary>
    /// E + F of a video drawn by the app (0.1.33): the scene plan (<see cref="Plans.ScenePlanner"/>: the code model writes each scene's base,
    /// points and beats; the image points model places them; the beats follow the spoken sentences), then the app's drawer
    /// (<see cref="Plans.PlanDrawer.Code"/>) is written without the code check (no model wrote it) and rendered. A render that fails falls back
    /// to the app's simple animation; scenes that fail the frame check keep the video with a warning (no paid repair for an app file).
    /// </summary>
    private async Task<RenderOutcome> DrawAsync(IVideoEngine engine, ResolvedModel codeModel, VideoOptions options, RenderInput input, string work, Guid id,
        VideoStepProgress steps, PlanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(codeModel);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(steps);
        var style = options.Style;
        RenderStyleBuilder.Apply(input, style);
        await BoostAsync(input, work, cancellationToken).ConfigureAwait(false);
        var project = Path.Combine(work, "project");
        PlaceFonts(project, style, input.Language);
        steps.Report(VideoSteps.Code, 0.1);
        await planner.PlanAsync(new Plans.PlanRequest(codeModel, context.PointsModel, input, style, context.Spans, context.ImagePrompts, project, id,
            VideoStyleRules.SubtitlesOn(options), MusicHints: context.Music is { Automatic: true }), steps,
            cancellationToken).ConfigureAwait(false);

        // 0.1.45: a library track chosen by the concept follows the plan's music hints; the scene changes move onto its beats before the render.
        // 0.1.46: generated music is made here from the same hints (after the narration), its price spent as the "music" line.
        var chosen = context.Music is { Automatic: true }
            ? await musicStep.SettleAsync(context.Music, style, input, context.Clips, work, align: true, cancellationToken, VideoMusicStep.Spender(steps, id))
                .ConfigureAwait(false)
            : null;
        steps.Finish(VideoSteps.Code);
        var request = new EngineRenderRequest(project, Plans.PlanDrawer.Code, RenderInputBuilder.ForEngine(input, VideoStyleRules.SubtitlesOn(options)),
            Path.Combine(work, "silent.mp4"), Workers());
        var engineProgress = new ForwardProgress<EngineEvent>(e => steps.Report(VideoSteps.Render, e.Ratio ?? 0, e.Message));
        var appCode = false;
        var finish = input.Style?.Finish;
        var renderWatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            engine.WriteProject(request);
            await engine.RenderAsync(request, engineProgress, cancellationToken).ConfigureAwait(false);

            // F12 (0.1.43): the render time next to the finishing layer, so its cost shows in the log (the same video with it off is the measure).
            logger.LogInformation("The app's drawer rendered in {Seconds:0.0} s ({PerSecond:0.00} s per video second); finishing layer {Finish}",
                renderWatch.Elapsed.TotalSeconds, renderWatch.Elapsed.TotalSeconds / Math.Max(1, input.DurationSeconds),
                finish is null ? "off" : string.Join("+", FinishNames(finish)));
        }
        catch (EngineException ex) when (ex.Code is "render" or "check")
        {
            logger.LogWarning("The app's scene drawer did not render ({Error}); the app's simple animation is used", ex.Message);
            request = AppCode(engine, request, style) ?? throw new VideoException("Render failed: " + ex.Message, ex) { Code = "render" };
            await RenderAppCodeAsync(engine, request, engineProgress, cancellationToken).ConfigureAwait(false);
            appCode = true;
        }
        catch (EngineException ex)
        {
            throw new VideoException("Render failed: " + ex.Message, ex) { Code = "render" };
        }

        var (findings, _) = await FrameFindingsAsync(request, cancellationToken).ConfigureAwait(false);
        if (findings.Count > 0)
        {
            logger.LogWarning(Activity.LogEvents.OnScreen, "The drawn video does not show {Failed} scenes well ({Findings}); it is kept with a warning",
                findings.Count, string.Join(" · ", findings));
        }

        steps.Finish(VideoSteps.Render);
        return new RenderOutcome(request, false, input) { AppCode = appCode, FrameWeak = findings.Count > 0, Music = chosen };
    }

    /// <summary>Renders the app's own animation (no repair: the app's code is not sent to a model).</summary>
    private static async Task RenderAppCodeAsync(IVideoEngine engine, EngineRenderRequest request, IProgress<EngineEvent> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await engine.RenderAsync(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (EngineException ex)
        {
            throw new VideoException("Render failed: " + ex.Message, ex) { Code = ex.Code == "check" ? "code" : "render" };
        }
    }

    /// <summary>
    /// The frame check of a rendered silent video (<see cref="FrameCheck"/>): one frame from the middle of each scene and each scene's image
    /// from the project folder, sampled small by ffmpeg; every scene's scores are logged. Returns the findings of the scenes that fail (empty =
    /// passed) and how many scenes were checked. A sample ffmpeg cannot read is logged and its scene is left out: the check never fails a
    /// video it could not see.
    /// </summary>
    private async Task<(IReadOnlyList<string> Findings, int Seen)> FrameFindingsAsync(EngineRenderRequest request, CancellationToken cancellationToken)
    {
        var scenes = request.Input.Scenes;
        if (scenes.Count == 0 || !File.Exists(request.Output))
        {
            return ([], 0);
        }

        IReadOnlyList<Media.FramePixels?> frames;
        try
        {
            frames = await media.SampleAsync(request.Output, scenes.Select(s => (long)Math.Round((s.StartSeconds + (s.DurationSeconds / 2)) * 1000)).ToList(),
                Media.FrameRegion.Whole, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The rendered video could not be sampled for the frame check; it is skipped");
            return ([], 0);
        }

        var findings = new List<string>();
        var scores = new List<string>();
        for (var i = 0; i < scenes.Count && i < frames.Count; i++)
        {
            if (frames[i] is not { } frame)
            {
                continue;
            }

            Media.FramePixels? image = null;
            // A scene drawn as a map, a diagram or a title (0.1.33) does not show its picture: only its emptiness is checked.
            var shown = scenes[i].Plan is not { } scenePlan || scenePlan.Base == Plans.PlanBases.Image;
            var file = scenes[i].Image.Length > 0 && shown ? Path.Combine(request.Folder, scenes[i].Image) : null;
            if (file is not null && File.Exists(file))
            {
                try
                {
                    image = (await media.SampleAsync(file, [0], Media.FrameRegion.Whole, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Scene {Number}'s image could not be sampled for the frame check", scenes[i].Number);
                }
            }

            var score = FrameCheck.Score(scenes[i].Number, frame, image);
            scores.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{score.Scene}: deviation {score.Deviation:0.0}{(score.Resemblance is { } r ? $", resemblance {r:0.00}" : string.Empty)}"));
            if (score.Failed)
            {
                findings.Add(score.Finding);
            }
        }

        logger.LogInformation("Frame check: {Scores}", string.Join(" · ", scores));
        return (findings, scores.Count);
    }

    /// <summary>A model that runs on this computer (free; small ones often fail the video code check): the app's own, its Ollama copy or a local server.</summary>
    public static bool IsLocal(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider is ModelProviders.Local or ModelProviders.AppOllama || Endpoints.EndpointCodes.IsEndpoint(model.Provider);
    }

    /// <summary>The project written with the engine's own code (<see cref="IVideoEngine.FallbackCode"/>); null when even that does not pass.</summary>
    private EngineRenderRequest? AppCode(IVideoEngine engine, EngineRenderRequest request, VideoStyle? style)
    {
        try
        {
            return Write(engine, request with { Code = engine.FallbackCode }, style);
        }
        catch (EngineException ex) when (ex.Code == "check")
        {
            logger.LogError("The app's own video code did not pass the check either: {Findings}", string.Join(" · ", ex.Findings.Take(4)));
            return null;
        }
    }

    /// <summary>
    /// F without a model: the kept <paramref name="code"/> is written with the new <paramref name="input"/> into <c>&lt;work&gt;/project</c>
    /// (whatever is already there, the scene images, stays) and rendered once. A failure is not repaired: a repair would be a paid call.
    /// Without subtitles the engine gets the input without captions, as at the first make; the video's template snapshot (when it has one) gives
    /// the input its tokens and the project its font again. The kept code already passed the template check when it was made.
    /// </summary>
    public async Task<RenderOutcome> RenderKeptAsync(IVideoEngine engine, string code, VideoOptions options, RenderInput input, string work,
        VideoStepProgress steps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(steps);
        RenderStyleBuilder.Apply(input, options.Style);
        if (!Plans.PlanDrawer.IsDrawer(code))
        {
            NoFinish(input);
        }

        await BoostAsync(input, work, cancellationToken).ConfigureAwait(false);
        PlaceFonts(Path.Combine(work, "project"), options.Style, input.Language);
        var request = new EngineRenderRequest(Path.Combine(work, "project"), code, RenderInputBuilder.ForEngine(input, VideoStyleRules.SubtitlesOn(options)),
            Path.Combine(work, "silent.mp4"), Workers());
        try
        {
            engine.WriteProject(request);
            await engine.RenderAsync(request, new ForwardProgress<EngineEvent>(e => steps.Report(VideoSteps.Render, e.Ratio ?? 0, e.Message)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (EngineException ex)
        {
            throw new VideoException("Render of the kept code failed: " + ex.Message, ex) { Code = ex.Code == "check" ? "code" : "render" };
        }

        steps.Finish(VideoSteps.Render);
        return new RenderOutcome(request, false, input);
    }

    /// <summary>
    /// H + I: subtitles from the input's captions, timed by the spoken parts around the pauses (<see cref="SubtitleSources"/>; burned in when chosen), the clips joined with the silent video into the work folder (with a
    /// fade through black at the start of each scene with a soft transition, see <see cref="Fades"/>), then
    /// the version folder gets only finished files — subtitles, the narration text, the engine project as it rendered and the clips, the
    /// video, the motion check's frame strips (0.1.41, <see cref="MotionAsync"/>), the thumbnail — and the record with its cost lines and
    /// warnings is saved.
    /// </summary>
    public async Task<VideoRecord> SaveAsync(VersionSave save, VideoStepProgress steps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(steps);
        var input = save.Input;
        var clips = save.Clips;
        var cueSources = input.Scenes.Select(s => new SubtitleSource((long)Math.Round(s.StartSeconds * 1000),
            clips.ElementAtOrDefault(s.Number - 1)?.DurationMs ?? (long)Math.Round(s.DurationSeconds * 1000), s.Caption)).ToList();
        // 0.1.48: the line leaves room for a growing highlighted word (F6), and the words are timed by the voice when whisper.cpp is there (F5).
        // Subtitles that are not drawn (a template without them, or subtitles off) are not heard: no whisper.cpp run for the SRT file alone.
        var timing = await subtitleTiming.BuildAsync(input, clips, save.Profile.ContentLanguage,
            SubtitleBuilder.LineLength(input.Width, input.Height, WordHighlight.GrowPercent(save.Options.Style)),
            save.Options.Style?.Subtitles ?? save.Options.Subtitles, cancellationToken).ConfigureAwait(false);
        var cues = timing.Cues;
        var overlay = await OverlayAsync(save, cues, cancellationToken).ConfigureAwait(false);
        if (save.Music is { } music)
        {
            overlay = overlay with { Music = MuxMusicOf(music, save.Options.Style!.Music, clips, input) };
        }

        var audio = clips.Select((c, i) => (Clip: c, Start: (long)Math.Round(input.Scenes[i].StartSeconds * 1000))).Where(x => x.Clip is not null)
            .Select(x => (x.Clip!.File, x.Start)).ToList();
        var muxed = Path.Combine(save.Work, "video.partial.mp4");
        steps.Report(VideoSteps.Mux, 0.1);
        try
        {
            await media.MuxAsync(save.Work, save.Render.Request.Output, audio, overlay, Fades(input), muxed, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new VideoException("Joining video and narration failed: " + ex.Message, ex) { Code = "mux" };
        }

        var folder = save.TargetFolder;
        save.Target.Folder = folder;
        save.Target.Created = !Directory.Exists(folder);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, VideoFolders.SubtitlesFile), SubtitleBuilder.Srt(cues), cancellationToken).ConfigureAwait(false);

        // The narration text is always kept (it was a choice until 0.1.6): a later version reads it again, edited or with another voice.
        await File.WriteAllTextAsync(Path.Combine(folder, VideoFolders.NarrationFile), NarrationScript.Text(save.Title, save.Profile.ContentLanguage,
            cueSources), cancellationToken).ConfigureAwait(false);
        KeepSources(save.Render.Request.Folder, folder, clips);
        await KeepMusicAsync(save.Music, folder, cancellationToken).ConfigureAwait(false);

        // The kept input.json has the real captions also when the engine got none (subtitles off): a later version and the Produced page's
        // narration read them from there.
        await File.WriteAllTextAsync(Path.Combine(folder, VideoFolders.Source, VideoFolders.InputFile), RenderInputBuilder.ToJson(input), cancellationToken)
            .ConfigureAwait(false);
        if (save.Script is { } script)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, VideoFolders.ScriptFile), JsonSerializer.Serialize(script.ToList(), CoreJsonContext.Default.ListString),
                cancellationToken).ConfigureAwait(false);
        }

        var output = Path.Combine(folder, VideoFolders.VideoFile);
        File.Move(muxed, output, overwrite: true);
        steps.Finish(VideoSteps.Mux);
        var moved = await MotionAsync(output, input, folder, save.Work, cancellationToken).ConfigureAwait(false);
        await KeepSubtitleCheckAsync(timing, folder, cancellationToken).ConfigureAwait(false);

        var thumbnail = await media.ThumbnailAsync(output, Path.Combine(folder, VideoFolders.ThumbnailFile), cancellationToken).ConfigureAwait(false);
        var record = new VideoRecord
        {
            Id = save.Id,
            ConceptId = save.ConceptId,
            ProfileId = save.Profile.Id,
            Title = save.Title,
            Folder = LibraryPaths.Relative(paths.LibraryDirectory, folder),
            FileName = VideoFolders.VideoFile,
            ThumbnailName = thumbnail ? VideoFolders.ThumbnailFile : string.Empty,
            DurationMs = (long)Math.Round(input.DurationSeconds * 1000),
            Width = input.Width,
            Height = input.Height,
            Fps = input.Fps,
            SizeBytes = new FileInfo(output).Length,
            Engine = save.EngineName,
            CodeModel = save.CodeModel,
            SpeechModel = save.SpeechModel,
            ImageModel = save.ImageModel,
            SettingsJson = JsonSerializer.Serialize(save.Options, CoreJsonContext.Default.VideoOptions),
            PromptVersion = save.PromptVersion,
            TotalUsd = save.Spent.Sum(c => c.Usd),
            CostExact = save.Spent.All(c => c.Exact),
            ElapsedMs = save.Watch.ElapsedMilliseconds,
            // No frame that is not black for the thumbnail: a yellow warning (until 0.1.29 only a log line, and an empty video was "ready").
            Warnings = string.Join(',', save.Warnings.Where(w => !VideoWarnings.Motion.Contains(w) && !VideoWarnings.Subtitles.Contains(w)).Concat(moved)
                .Concat(timing.Warnings)
                .Concat(thumbnail ? [] : [VideoWarnings.NoThumbnail]).Distinct()),
            CreatedAtUtc = clock.GetUtcNow(),
        };
        await videos.AddAsync(record, [.. save.ExtraCosts, .. save.Spent], CancellationToken.None).ConfigureAwait(false);
        steps.Finish(VideoSteps.Save);
        return record;
    }

    /// <summary>
    /// The template's background music for a run, checked before anything is paid for (<see cref="VideoMusicStep.PrepareAsync"/>): off =
    /// <c>null</c>; a missing or unreadable file, or a library that is not installed, stops the video with <c>music</c> (rule 17).
    /// </summary>
    public Task<PreparedMusic?> PrepareMusicAsync(VideoStyle? style, string? keptVersion, string work, CancellationToken cancellationToken) =>
        musicStep.PrepareAsync(style, keptVersion, work, cancellationToken);

    /// <summary>The music kept in a version folder (the first file of its <see cref="VideoFolders.Music"/> folder); null when it has none.</summary>
    public static string? KeptMusic(string versionFolder)
    {
        var folder = Path.Combine(versionFolder, VideoFolders.Music);
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal).FirstOrDefault() : null;
    }

    /// <summary>
    /// The mux's music: looped when the track is shorter than the video, ducked under the narration (each clip's spoken parts at its start;
    /// the whole clip when its format cannot be read), faded out at the end.
    /// </summary>
    private static MuxMusic MuxMusicOf(PreparedMusic prepared, VideoMusic settings, IReadOnlyList<SpeechClip?> clips, RenderInput input)
    {
        var spoken = clips.Select((c, i) => (Clip: c, Start: (long)Math.Round(input.Scenes[i].StartSeconds * 1000)))
            .Where(x => x.Clip is not null)
            .Select(x => (x.Start, SpokenOf(x.Clip!)));
        var spans = MusicEnvelope.Merge(MusicEnvelope.Timeline(spoken));
        var (level, ducked, fade) = VideoStyleRules.MusicMix(settings);
        var length = input.DurationSeconds;
        if (prepared.Placement is not { } place)
        {
            return new MuxMusic(prepared.File, prepared.DurationMs < (long)Math.Round(length * 1000), length, MusicEnvelope.Expression(spans, level, ducked), fade);
        }

        // 0.1.45 (F8): the laid-out track (entry point, loop copies) when ffmpeg made it, under the scenes the plan chose.
        var file = place.BedFile ?? prepared.File;
        var playing = (place.EndSeconds ?? length) - place.StartSeconds;
        var seconds = place.BedFile is null ? prepared.DurationMs / 1000.0 : place.BedSeconds;
        return new MuxMusic(file, seconds + 0.001 < playing, length, MusicEnvelope.Expression(spans, level, ducked), place.EndSeconds is null ? fade : place.EndFadeSeconds,
            place.StartSeconds, place.EndSeconds, place.FadeInSeconds);
    }

    /// <summary>The spoken parts of a clip; the whole clip when its file cannot be read as a WAV this reads (the music ducks under all of it).</summary>
    private static IReadOnlyList<NarrationSpan> SpokenOf(SpeechClip clip)
    {
        try
        {
            return NarrationSilence.Spoken(clip.File) ?? [new NarrationSpan(0, clip.DurationMs)];
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            return [new NarrationSpan(0, clip.DurationMs)];
        }
    }

    /// <summary>
    /// The version's music copy and asset record (F15): the track into <see cref="VideoFolders.Music"/>, every outside file's line into
    /// <see cref="VideoFolders.AssetsFile"/> (an empty list without music).
    /// </summary>
    private static async Task KeepMusicAsync(PreparedMusic? music, string folder, CancellationToken cancellationToken)
    {
        var entries = new List<AssetEntry>();
        if (music is not null)
        {
            var target = Path.Combine(folder, VideoFolders.Music);
            Directory.CreateDirectory(target);
            File.Copy(music.File, Path.Combine(target, music.Entry.File), overwrite: true);
            entries.Add(music.Entry);
        }

        await AssetLedger.WriteAsync(folder, entries, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The motion check of the finished video (F3, 0.1.41; <see cref="MotionCheck"/>): every frame read small and grey by the app's ffmpeg,
    /// the findings logged with their times, a frame strip of each (at most <see cref="MotionCheckLimits.MaxStrips"/>) written into the
    /// version's <see cref="VideoFolders.Checks"/> folder; returns the warning codes found (yellow, the video is kept). A check that cannot
    /// run (no ffmpeg, an unreadable file) is logged and skipped: it never fails a video. No model is called.
    /// </summary>
    private async Task<IReadOnlyList<string>> MotionAsync(string video, RenderInput input, string folder, string work, CancellationToken cancellationToken)
    {
        // The strips belong to this video only: an existing folder's older strips would show findings it does not have.
        var checks = Path.Combine(folder, VideoFolders.Checks);
        TryDelete(checks);
        IReadOnlyList<MotionFinding> findings;
        try
        {
            var series = await motion.ReadAsync(video, input.Fps, work, cancellationToken).ConfigureAwait(false);
            if (series is null)
            {
                return [];
            }

            findings = MotionCheck.Find(series, MotionCheck.Expect(input));
            logger.LogInformation("Motion check: {Frames} frames, {Count} findings", series.Count, findings.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation("The motion check was skipped ({Error})", ex.Message);
            return [];
        }

        if (findings.Count == 0)
        {
            return [];
        }

        // Logged for the file only: the activity panel gets the warnings in the user's language from the codes (the video page).
        logger.LogWarning(Activity.LogEvents.OnScreen, "Motion check findings: {Findings}", string.Join(" · ", findings));
        foreach (var finding in findings.Take(MotionCheckLimits.MaxStrips))
        {
            try
            {
                await motion.StripAsync(video, finding.StripAt, finding.StripSeconds, input.Fps, Path.Combine(checks, finding.StripName), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogInformation("The frame strip of {Finding} was not written ({Error})", finding, ex.Message);
            }
        }

        return findings.Select(f => f.Code).Distinct().ToList();
    }

    /// <summary>
    /// What the subtitles show and when: a scene whose clip was joined from parts with pauses gets one source per spoken part at that part's
    /// time (a pause at the start or in the middle shows no subtitle, and the text does not run ahead of the voice); any other scene one
    /// source over its clip (or its planned length when silent).
    /// </summary>
    public static IReadOnlyList<SubtitleSource> SubtitleSources(RenderInput input, IReadOnlyList<SpeechClip?> clips)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(clips);
        return input.Scenes.SelectMany(scene => SceneSources(scene, clips.ElementAtOrDefault(scene.Number - 1))).ToList();
    }

    /// <summary>What the subtitles of one scene show and when (<see cref="SubtitleSources"/>; <paramref name="clip"/> is the scene's own).</summary>
    public static IReadOnlyList<SubtitleSource> SceneSources(RenderScene scene, SpeechClip? clip)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var start = (long)Math.Round(scene.StartSeconds * 1000);
        return clip is { Spans.Count: > 0 }
            ? clip.Spans.Select(s => new SubtitleSource(start + s.StartMs, s.DurationMs, s.Text)).ToList()
            : [new SubtitleSource(start, clip?.DurationMs ?? (long)Math.Round(scene.DurationSeconds * 1000), scene.Caption)];
    }

    /// <summary>
    /// Writes the sentences to listen to (<see cref="SubtitleTimingStep.CheckText"/>) into the version's checks folder when there is one
    /// (after the motion check, which starts that folder afresh). A file that cannot be written is logged; the video is kept.
    /// </summary>
    private async Task KeepSubtitleCheckAsync(SubtitleTimingResult timing, string folder, CancellationToken cancellationToken)
    {
        var text = SubtitleTimingStep.CheckText(timing.Sentences);
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            var checks = Path.Combine(folder, VideoFolders.Checks);
            Directory.CreateDirectory(checks);
            await File.WriteAllTextAsync(Path.Combine(checks, VideoFolders.SubtitleCheckFile), text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The subtitle check list was not written");
        }
    }

    /// <summary>The starts of the scenes that begin with a soft transition (ffmpeg fades through black there at the mux).</summary>
    public static IReadOnlyList<long> Fades(RenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Scenes.Where(s => s.Number > 1 && s.Transition == Scenes.SceneTransitions.Fade).Select(s => (long)Math.Round(s.StartSeconds * 1000)).ToList();
    }

    /// <summary>
    /// Runs the body of a video run with the failure handling every run shares: a failure or a cancel removes what the run wrote
    /// (<see cref="CleanFailed"/>), keeps what it paid (<paramref name="steps"/>' costs) as a run (<see cref="KeepLostAsync"/>, under
    /// <paramref name="payer"/>'s provider) and goes up as a <see cref="VideoException"/> with its code and what was spent (a cancel goes up as
    /// it is).
    /// </summary>
    public async Task<VideoRecord> GuardAsync(Guid profileId, ResolvedModel? payer, string work, VersionTarget target, VideoStepProgress steps,
        System.Diagnostics.Stopwatch watch, Func<Task<VideoRecord>> body)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (VideoException ex)
        {
            logger.LogError(Activity.LogEvents.OnScreen, "Video failed ({Code}): {Message}", ex.Code, ex.Message);
            CleanFailed(work, target);
            await KeepLostAsync(profileId, payer, steps.Spent, ex.Code, RunStatus.Failed, watch.Elapsed).ConfigureAwait(false);
            throw new VideoException(ex.Message, ex) { Code = ex.Code, SpentUsd = steps.SpentUsd };
        }
        catch (Providers.ProviderException ex)
        {
            logger.LogError(Activity.LogEvents.OnScreen, "Video failed: provider error ({Code}, request {RequestId}) after {Usd} USD", ex.Code, ex.RequestId.Length > 0 ? ex.RequestId : "-",
                steps.SpentUsd);
            CleanFailed(work, target);
            await KeepLostAsync(profileId, payer, steps.Spent, VideoException.ProviderPrefix + ex.Code, RunStatus.Failed, watch.Elapsed).ConfigureAwait(false);
            throw new VideoException(ex.Message, ex) { Code = VideoException.ProviderPrefix + ex.Code, SpentUsd = steps.SpentUsd };
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Video canceled after {Usd} USD were spent", steps.SpentUsd);
            CleanFailed(work, target);
            await KeepLostAsync(profileId, payer, steps.Spent, "canceled", RunStatus.Canceled, watch.Elapsed).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(Activity.LogEvents.OnScreen, ex, "Video failed: a file could not be written");
            CleanFailed(work, target);
            await KeepLostAsync(profileId, payer, steps.Spent, "files", RunStatus.Failed, watch.Elapsed).ConfigureAwait(false);
            throw new VideoException("A file could not be written: " + ex.Message, ex) { Code = "files", SpentUsd = steps.SpentUsd };
        }
    }

    /// <summary>
    /// Keeps what a failed or canceled run paid as a run of its own (nothing when it paid nothing). A failure to write it is logged and
    /// does not hide the run's own failure.
    /// </summary>
    public async Task KeepLostAsync(Guid profileId, ResolvedModel? model, IReadOnlyList<VideoCost> spent, string code, RunStatus status, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(spent);
        var usd = spent.Sum(c => c.Usd);
        if (usd <= 0)
        {
            return;
        }

        try
        {
            var now = clock.GetUtcNow();
            await runs.AddAsync(new Run
            {
                ProfileId = profileId,
                Kind = RunKind.FailedVideo,
                Provider = model?.Model.Provider ?? string.Empty,
                Model = model?.Model.Id ?? string.Empty,
                KeyLabel = model?.Key.Label ?? string.Empty,
                Requested = 1,
                ActualUsd = usd,
                CostExact = spent.All(c => c.Exact),
                StartedAtUtc = now - elapsed,
                FinishedAtUtc = now,
                Status = status,
                Error = code,
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "What the failed video paid ({Usd} USD) could not be kept", usd);
        }
    }

    /// <summary>
    /// After a failure or a cancel: the library keeps nothing of this run (the version folder it created is removed, and the concept's folder
    /// when nothing else is in it; in an existing folder only what it wrote), and the work folder is emptied except a rejected code, which
    /// stays for reading until the start-up clean-up.
    /// </summary>
    public void CleanFailed(string work, VersionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Folder is { } folder)
        {
            if (target.Created)
            {
                TryDelete(folder);
                var parent = Path.GetDirectoryName(folder);
                if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                {
                    TryDelete(parent);
                }
            }
            else
            {
                foreach (var name in new[] { VideoFolders.SubtitlesFile, VideoFolders.NarrationFile, VideoFolders.VideoFile, VideoFolders.ThumbnailFile, VideoFolders.ScriptFile })
                {
                    TryDeleteFile(Path.Combine(folder, name));
                }

                TryDelete(Path.Combine(folder, VideoFolders.Source));
                TryDelete(Path.Combine(folder, VideoFolders.Audio));
                TryDelete(Path.Combine(folder, VideoFolders.Checks));
            }
        }

        if (!Directory.Exists(work))
        {
            return;
        }

        if (!Directory.EnumerateFiles(work, Rejected + "*.txt").Any())
        {
            TryDelete(work);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(work).Where(e => !Path.GetFileName(e).StartsWith(Rejected, StringComparison.Ordinal)))
        {
            if (Directory.Exists(entry))
            {
                TryDelete(entry);
            }
            else
            {
                TryDeleteFile(entry);
            }
        }
    }

    /// <summary>Removes a folder; a file still held (a process that was just killed) is logged and left for the start-up clean-up.</summary>
    public void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A process that was just killed can hold a file for a moment; the start-up clean-up removes it later.
            logger.LogWarning(ex, "A work folder could not be removed yet");
        }
    }

    /// <summary>Copies a folder with its sub folders (the engine's temporary files and packages are left out).</summary>
    public static void CopyProject(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }

        // tmp/ holds the engine's frame cache and Node's temporary files; node_modules is never in a project (packages resolve from
        // the engine folder) but is skipped all the same. The template's fonts are the app's own files, placed again at every render.
        foreach (var folder in Directory.EnumerateDirectories(from).Where(d => Path.GetFileName(d) is not ("tmp" or "node_modules" or VideoFontFiles.Folder)))
        {
            CopyProject(folder, Path.Combine(to, Path.GetFileName(folder)));
        }
    }

    /// <summary>The name of the channel photo copied into a work folder (its extension follows).</summary>
    public const string PhotoFile = "channel-photo";

    /// <summary>The start of the message of a code that breaks the template (told apart from a safety finding in the final error).</summary>
    private const string TemplateRejection = "The video code does not follow the template: ";

    /// <summary>
    /// The template's font in the project: in <c>fonts/</c> (HyperFrames) and <c>public/fonts/</c> (Remotion's staticFile, Revideo's Vite
    /// server). Nothing without a template.
    /// </summary>
    private static void PlaceFonts(string project, VideoStyle? style, string language)
    {
        if (style is null)
        {
            return;
        }

        var font = VideoStyleRules.Font(style, language);
        VideoFontFiles.CopyTo(font, Path.Combine(project, VideoFontFiles.Folder));
        VideoFontFiles.CopyTo(font, Path.Combine(project, "public", VideoFontFiles.Folder));
    }

    /// <summary>
    /// Writes the project after the safety check (the engine's) and, with a template, the template check. Both run before anything is
    /// rejected, so one <see cref="EngineException"/> "check" carries every finding and the caller's single repair sees them all (before the
    /// 0.1.19 review the template check ran only on code that passed the safety check: a repair that fixed the safety finding then failed on
    /// a template rule it had never been told about). The message starts with <see cref="TemplateRejection"/> only when every finding is a
    /// template one. Colours outside the palette are replaced first, in the code that is checked and written.
    /// </summary>
    private EngineRenderRequest Write(IVideoEngine engine, EngineRenderRequest request, VideoStyle? style)
    {
        var images = ImageUseCheck.Finding(engine.Kind, request.Code, request.Input);
        if (style is not null)
        {
            var result = VideoStyleCheck.Check(request.Code, style, request.Input.Language);
            var safety = EngineCodeCheck.Check(engine.Kind, result.Code);
            if (safety.Count > 0 || result.Findings.Count > 0 || images is not null)
            {
                var findings = safety.Concat(result.Findings).Concat(images is null ? [] : [images]).ToList();
                var message = safety.Count > 0 ? $"{engine.Name} code did not pass the safety check: "
                    : result.Findings.Count > 0 ? TemplateRejection : ImageRejection;
                throw new EngineException(message + string.Join(" · ", findings.Take(6))) { Code = "check", Findings = findings };
            }

            if (result.Fixes.Count > 0)
            {
                logger.LogInformation("{Count} colour(s) outside the template's palette were replaced: {Fixes}", result.Fixes.Count,
                    string.Join(" · ", result.Fixes.Take(8)));
                request = request with { Code = result.Code };
            }
        }

        else if (images is not null)
        {
            var findings = EngineCodeCheck.Check(engine.Kind, request.Code).Append(images).ToList();
            var message = findings.Count > 1 ? $"{engine.Name} code did not pass the safety check: " : ImageRejection;
            throw new EngineException(message + string.Join(" · ", findings.Take(6))) { Code = "check", Findings = findings };
        }

        engine.WriteProject(request);
        return request;
    }

    /// <summary>The start of the message of a code that only fails to show the scene images.</summary>
    private const string ImageRejection = "The video code does not show the scene images: ";

    /// <summary>
    /// What the mux draws over the video. A video without a template: the subtitles in the old look when they are on (exactly as before). With
    /// one: <see cref="StyleOverlay"/> (subtitles, progress bar, closing card) with the template's font next to it, and the channel photo on
    /// the card. A closing whose language has no sentences is skipped with a warning.
    /// </summary>
    private async Task<MuxOverlay> OverlayAsync(VersionSave save, IReadOnlyList<Subtitles.SubtitleCue> cues, CancellationToken cancellationToken)
    {
        var input = save.Input;
        var style = save.Options.Style;
        if (style is null)
        {
            if (!save.Options.Subtitles || cues.Count == 0)
            {
                return MuxOverlay.None;
            }

            await File.WriteAllTextAsync(Path.Combine(save.Work, AssFile), SubtitleBuilder.Ass(cues, input.Width, input.Height, input.Theme), cancellationToken)
                .ConfigureAwait(false);
            return new MuxOverlay(AssFile);
        }

        if (VideoStyleRules.FontMissing(style))
        {
            logger.LogWarning("The template's font is not installed on this computer; the video uses the default font ({Font})",
                VideoStyleRules.Font(style, input.Language).Family);
        }

        if (VideoStyleRules.ClosingMissing(style, input.Language))
        {
            logger.LogWarning("The template's closing has no sentences in the video's language ({Language}); the video ends without it", input.Language);
        }

        var closing = VideoStyleRules.Closing(style, input.Language);
        var photo = closing is { Screen: true } ? PlacePhoto(save.Profile, save.Work) : null;
        var needs = style.ReadableText && style.Subtitles && cues.Count > 0
            ? await SubtitleNeedsAsync(save.Render.Request.Output, cues, input, VideoStyleRules.Palette(style).Text, cancellationToken).ConfigureAwait(false)
            : null;
        var drawn = StyleOverlay.Build(input.Width, input.Height, input.DurationSeconds, cues, style, input.Language, closing, save.Profile.Name, photo is not null,
            needs);
        if (drawn.Ass is null)
        {
            return MuxOverlay.None;
        }

        await File.WriteAllTextAsync(Path.Combine(save.Work, AssFile), drawn.Ass, cancellationToken).ConfigureAwait(false);
        VideoFontFiles.CopyTo(VideoStyleRules.Font(style, input.Language), Path.Combine(save.Work, VideoFontFiles.Folder));
        var card = drawn.Card;
        return new MuxOverlay(AssFile, VideoFontFiles.Folder, photo is not null && card is not null
            ? new MuxPhoto(photo, card.PhotoX, card.PhotoY, card.PhotoSize, card.StartSeconds, card.EndSeconds, StyleLayout.EndCardFadeSeconds, input.Fps)
            : null);
    }

    /// <summary>
    /// The profile's channel photo copied into the work folder (its name there), or <c>null</c> when the profile has none. A photo whose file
    /// is gone (or lies outside the library) is logged and the card is drawn without it.
    /// </summary>
    private string? PlacePhoto(Profiles.Profile profile, string work)
    {
        if (string.IsNullOrWhiteSpace(profile.ChannelPhoto))
        {
            return null;
        }

        string source;
        try
        {
            source = LibraryPaths.Resolve(paths.LibraryDirectory, profile.ChannelPhoto);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "The channel photo's path is outside the library folder; the closing card is drawn without it");
            return null;
        }

        if (!File.Exists(source))
        {
            logger.LogWarning("The channel photo file is missing; the closing card is drawn without it");
            return null;
        }

        var name = PhotoFile + Path.GetExtension(source).ToLowerInvariant();
        File.Copy(source, Path.Combine(work, name), overwrite: true);
        return name;
    }

    /// <summary>
    /// What each subtitle needs over the rendered video behind it (<see cref="TextBackdrop"/>): a frame at the middle of each subtitle (at
    /// most <see cref="TextBackdrop.MaxSamples"/>) is read under the subtitles' place and measured against the text colour. A frame that
    /// cannot be read leaves its subtitles to their neighbours; when ffmpeg cannot read the video at all, the cause is logged and every
    /// subtitle keeps the normal readable edge (the video is still made).
    /// </summary>
    private async Task<IReadOnlyList<TextBackdropNeed>?> SubtitleNeedsAsync(string video, IReadOnlyList<Subtitles.SubtitleCue> cues, RenderInput input,
        string textColour, CancellationToken cancellationToken)
    {
        var indexes = TextBackdrop.SampleIndexes(cues.Count);
        IReadOnlyList<Media.FramePixels?> frames;
        try
        {
            frames = await media.SampleAsync(video, indexes.Select(i => (cues[i].StartMs + cues[i].EndMs) / 2).ToList(),
                TextBackdrop.SubtitleRegion(input.Width, input.Height), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The video behind the subtitles could not be measured; they keep the normal readable outline");
            return null;
        }

        var sampled = new Dictionary<int, TextBackdropNeed>();
        for (var i = 0; i < indexes.Count && i < frames.Count; i++)
        {
            if (frames[i] is { } frame)
            {
                sampled[indexes[i]] = TextBackdrop.Need(TextBackdrop.Measure(frame, textColour));
            }
        }

        var needs = TextBackdrop.Spread(cues.Count, sampled);
        logger.LogInformation("Subtitles over the video: {Plain} plain, {Stronger} with a stronger outline, {Backdrop} with a backdrop ({Sampled} of {Count} measured)",
            needs.Count(n => n == TextBackdropNeed.Plain), needs.Count(n => n == TextBackdropNeed.Stronger), needs.Count(n => n == TextBackdropNeed.Backdrop),
            sampled.Count, cues.Count);
        return needs;
    }

    /// <summary>
    /// The on-screen text boost of a video with readable text (<see cref="RenderStyle.OutlineBoost"/>): every scene image is measured as a
    /// whole against the text colour (where the code puts its text is not known) and the strongest need wins, so the text stays readable on
    /// every scene. Scenes without an image keep the normal outline (the code draws on the template's own colours). An image that cannot be
    /// read is logged and left out.
    /// </summary>
    private async Task BoostAsync(RenderInput input, string work, CancellationToken cancellationToken)
    {
        if (input.Style is not { Outline.Length: > 0 } style)
        {
            return;
        }

        var strongest = TextBackdropNeed.Plain;
        foreach (var scene in input.Scenes.Where(s => s.Image.Length > 0))
        {
            // The images lie in the project folder (and its public/), not in the work folder itself: until 0.1.29 every sample failed.
            var file = Path.Combine(work, "project", scene.Image);
            try
            {
                var frames = await media.SampleAsync(file, [0], Media.FrameRegion.Whole, cancellationToken).ConfigureAwait(false);
                if (frames.Count > 0 && frames[0] is { } frame)
                {
                    var need = TextBackdrop.Need(TextBackdrop.Measure(frame, style.Colors.Text));
                    strongest = need > strongest ? need : strongest;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Scene {Number}'s image could not be measured for readable text", scene.Number);
            }
        }

        style.OutlineBoost = TextBackdrop.Boost(strongest);
        logger.LogInformation("On-screen text over the scene images: {Need} (outline boost {Boost})", strongest, style.OutlineBoost);
    }

    /// <summary>The overlay file's name in the work folder.</summary>
    private const string AssFile = "subtitles.ass";

    private async Task<string> CodeAsync(IChatClient chat, ResolvedModel codeModel, string system, string user, VideoStepProgress steps, Guid id,
        CancellationToken cancellationToken)
    {
        // Logged before the call, as the concept run does: a code answer can take 10 minutes and be cut at max_tokens (thinking counts
        // toward it), so the log must say what budget and effort it ran with even when it never returns.
        var maxTokens = VideoCodeBudget.For(codeModel.Model);
        logger.LogInformation("Video {Video}: code with {Provider}/{Model}, max_tokens {MaxTokens}, effort {Effort}", id, codeModel.Model.Provider,
            codeModel.Model.Id, maxTokens, codeModel.Effort.Length > 0 ? codeModel.Effort : "automatic");
        ChatResponse answer;
        try
        {
            // The call's phase and tokens go to the screen with the run's progress (a code answer can take minutes).
            answer = await chat.SendAsync(new ChatRequest(system, user, CacheSystem: true, MaxOutputTokens: maxTokens,
                Effort: codeModel.Effort, Stream: true, Progress: new ForwardProgress<ChatProgress>(steps.Call)), cancellationToken).ConfigureAwait(false);
        }
        catch (Providers.ProviderException ex) when (ex.ChargedUsage is { } charged)
        {
            // A cut-off or refused code answer is billed: counted before the failure goes up.
            steps.Spend(new VideoCost { VideoId = id, Step = PipelineSteps.VideoCode, Usd = costs.Exact(codeModel.Model, charged), Exact = true });
            throw;
        }

        steps.Spend(new VideoCost
        {
            VideoId = id,
            Step = PipelineSteps.VideoCode,
            Usd = costs.Exact(codeModel.Model, answer.Usage, answer.ReportedCostUsd),
            Exact = true,
        });
        return EngineCodeCheck.ExtractCode(answer.Text);
    }

    /// <summary>
    /// Keeps what a later version needs to render the same code again without a model call: the engine project as it rendered (skeleton,
    /// the model's code, input.json, scene images; not its temporary files) and the scene clips (WAV, as the voice made them).
    /// </summary>
    private static void KeepSources(string project, string version, IReadOnlyList<SpeechClip?> clips)
    {
        CopyProject(project, Path.Combine(version, VideoFolders.Source));
        foreach (var clip in clips.OfType<SpeechClip>())
        {
            var audio = Path.Combine(version, VideoFolders.Audio);
            Directory.CreateDirectory(audio);
            File.Copy(clip.File, Path.Combine(audio, Path.GetFileName(clip.File)), overwrite: true);
        }
    }

    private void TryDeleteFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "A file of a failed video could not be removed");
        }
    }
}
