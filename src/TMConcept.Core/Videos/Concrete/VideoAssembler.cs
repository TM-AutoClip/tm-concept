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
    TimeProvider clock, ILogger<VideoAssembler> logger)
{
    /// <summary>The start of a rejected code's file name in a work folder (kept for reading until the start-up clean-up).</summary>
    public const string Rejected = "rejected-";

    /// <summary>The work folder of a run.</summary>
    public string WorkFolder(Guid id) => Path.Combine(paths.WorkDirectory, "videos", id.ToString("N"));

    /// <summary>Render workers: cores − 2 (at least 1).</summary>
    public static int Workers() => Math.Clamp(Environment.ProcessorCount - 2, 1, 8);

    /// <summary>
    /// E + F: the video code from <paramref name="codeModel"/> for <paramref name="input"/> (paid; every call is spent in
    /// <paramref name="steps"/>, also a cut-off one; the call's phase and tokens are reported while it streams), written into
    /// <c>&lt;work&gt;/project</c> after the safety check (a rejected code gets one repair), then the render (a failed render gets one repair with
    /// its error text, unless the code was repaired already). Without subtitles (<see cref="VideoStyleRules.SubtitlesOn"/>) the prompt and the
    /// engine get the input without captions (<see cref="RenderInputBuilder.ForEngine"/>), so the code cannot draw the narration. With a
    /// template (<see cref="VideoOptions.Style"/>) the input gets its tokens (<see cref="RenderStyleBuilder"/>), the prompt its block, the project
    /// its font, and the code the template check after the safety check (<see cref="VideoStyleCheck"/>: a finding takes the same single repair;
    /// colours outside the palette are replaced without one).
    /// </summary>
    public async Task<RenderOutcome> CodeAndRenderAsync(IVideoEngine engine, ResolvedModel codeModel, VideoOptions options, RenderInput input, string work,
        Guid id, VideoStepProgress steps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(codeModel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(steps);
        var subtitles = VideoStyleRules.SubtitlesOn(options);
        var style = options.Style;
        RenderStyleBuilder.Apply(input, style);
        var chat = chats.Create(codeModel.Model, codeModel.Key.Secret);
        var system = VideoCodePrompt.System(engine, subtitles, style, input.Language);
        var repaired = false;
        steps.Report(VideoSteps.Code, 0.1);
        var engineInput = RenderInputBuilder.ForEngine(input, subtitles);
        PlaceFonts(Path.Combine(work, "project"), style, input.Language);
        var code = await CodeAsync(chat, codeModel, system, VideoCodePrompt.User(engineInput), steps, id, cancellationToken).ConfigureAwait(false);
        var request = new EngineRenderRequest(Path.Combine(work, "project"), code, engineInput, Path.Combine(work, "silent.mp4"), Workers());
        try
        {
            request = Write(engine, request, style);
        }
        catch (EngineException ex) when (ex.Code == "check")
        {
            await File.WriteAllTextAsync(Path.Combine(work, Rejected + "1.txt"), code, cancellationToken).ConfigureAwait(false);
            logger.LogWarning("The video code was rejected; asking for one repair: {Findings}", string.Join(" · ", ex.Findings.Take(4)));
            code = await CodeAsync(chat, codeModel, system, VideoCodePrompt.Repair(code, string.Join("\n", ex.Findings)), steps, id, cancellationToken)
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
                var what = again.Message.StartsWith(TemplateRejection, StringComparison.Ordinal)
                    ? "The video code did not follow the template twice and was not run: "
                    : "The video code did not pass the safety check twice and was not run: ";
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
            catch (EngineException again)
            {
                throw new VideoException("Render failed after the repair: " + again.Message, again) { Code = again.Code == "check" ? "code" : "render" };
            }
        }
        catch (EngineException ex)
        {
            throw new VideoException("Render failed: " + ex.Message, ex) { Code = ex.Code == "check" ? "code" : "render" };
        }

        steps.Finish(VideoSteps.Render);
        return new RenderOutcome(request, repaired, input);
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
    /// video, the thumbnail — and the record with its cost lines is saved.
    /// </summary>
    public async Task<VideoRecord> SaveAsync(VersionSave save, VideoStepProgress steps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(steps);
        var input = save.Input;
        var clips = save.Clips;
        var cueSources = input.Scenes.Select(s => new SubtitleSource((long)Math.Round(s.StartSeconds * 1000),
            clips.ElementAtOrDefault(s.Number - 1)?.DurationMs ?? (long)Math.Round(s.DurationSeconds * 1000), s.Caption)).ToList();
        var cues = SubtitleBuilder.Build(SubtitleSources(input, clips), SubtitleBuilder.LineLength(input.Width, input.Height));
        var overlay = await OverlayAsync(save, cues, cancellationToken).ConfigureAwait(false);

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
            Warnings = string.Join(',', save.Warnings.Distinct()),
            CreatedAtUtc = clock.GetUtcNow(),
        };
        await videos.AddAsync(record, [.. save.ExtraCosts, .. save.Spent], CancellationToken.None).ConfigureAwait(false);
        steps.Finish(VideoSteps.Save);
        return record;
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
        var sources = new List<SubtitleSource>();
        foreach (var scene in input.Scenes)
        {
            var start = (long)Math.Round(scene.StartSeconds * 1000);
            var clip = clips.ElementAtOrDefault(scene.Number - 1);
            if (clip is { Spans.Count: > 0 })
            {
                sources.AddRange(clip.Spans.Select(s => new SubtitleSource(start + s.StartMs, s.DurationMs, s.Text)));
                continue;
            }

            sources.Add(new SubtitleSource(start, clip?.DurationMs ?? (long)Math.Round(scene.DurationSeconds * 1000), scene.Caption));
        }

        return sources;
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
    /// Writes the project after the safety check (the engine's) and, with a template, the template check: a finding is an
    /// <see cref="EngineException"/> "check" (the caller's single repair); colours outside the palette are replaced in the code that is written.
    /// </summary>
    private EngineRenderRequest Write(IVideoEngine engine, EngineRenderRequest request, VideoStyle? style)
    {
        if (style is not null && EngineCodeCheck.Check(engine.Kind, request.Code).Count == 0)
        {
            var result = VideoStyleCheck.Check(request.Code, style, request.Input.Language);
            if (result.Findings.Count > 0)
            {
                throw new EngineException(TemplateRejection + string.Join(" · ", result.Findings.Take(6))) { Code = "check", Findings = result.Findings };
            }

            if (result.Fixes.Count > 0)
            {
                logger.LogInformation("{Count} colour(s) outside the template's palette were replaced: {Fixes}", result.Fixes.Count,
                    string.Join(" · ", result.Fixes.Take(8)));
                request = request with { Code = result.Code };
            }
        }

        engine.WriteProject(request);
        return request;
    }

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

        if (VideoStyleRules.ClosingMissing(style, input.Language))
        {
            logger.LogWarning("The template's closing has no sentences in the video's language ({Language}); the video ends without it", input.Language);
        }

        var closing = VideoStyleRules.Closing(style, input.Language);
        var photo = closing is { Screen: true } ? PlacePhoto(save.Profile, save.Work) : null;
        var drawn = StyleOverlay.Build(input.Width, input.Height, input.DurationSeconds, cues, style, input.Language, closing, save.Profile.Name, photo is not null);
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
