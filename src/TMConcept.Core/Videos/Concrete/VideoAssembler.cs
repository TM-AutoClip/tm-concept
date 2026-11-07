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
    /// E + F: the video code from <paramref name="codeModel"/> for <paramref name="input"/> (paid; every call is in <paramref name="spent"/>,
    /// also a cut-off one), written into <c>&lt;work&gt;/project</c> after the safety check (a rejected code gets one repair), then the render
    /// (a failed render gets one repair with its error text, unless the code was repaired already).
    /// </summary>
    public async Task<RenderOutcome> CodeAndRenderAsync(IVideoEngine engine, ResolvedModel codeModel, bool subtitles, RenderInput input, string work,
        List<VideoCost> spent, Guid id, VideoStepProgress steps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(codeModel);
        ArgumentNullException.ThrowIfNull(steps);
        var chat = chats.Create(codeModel.Model, codeModel.Key.Secret);
        var system = VideoCodePrompt.System(engine, subtitles);
        var repaired = false;
        steps.Report("code", 0.1);
        var code = await CodeAsync(chat, codeModel, system, VideoCodePrompt.User(input), spent, id, cancellationToken).ConfigureAwait(false);
        var request = new EngineRenderRequest(Path.Combine(work, "project"), code, input, Path.Combine(work, "silent.mp4"), Workers());
        try
        {
            engine.WriteProject(request);
        }
        catch (EngineException ex) when (ex.Code == "check")
        {
            await File.WriteAllTextAsync(Path.Combine(work, Rejected + "1.txt"), code, cancellationToken).ConfigureAwait(false);
            logger.LogWarning("The video code was rejected; asking for one repair: {Findings}", string.Join(" · ", ex.Findings.Take(4)));
            code = await CodeAsync(chat, codeModel, system, VideoCodePrompt.Repair(code, string.Join("\n", ex.Findings)), spent, id, cancellationToken)
                .ConfigureAwait(false);
            request = request with { Code = code };
            repaired = true;
            try
            {
                engine.WriteProject(request);
            }
            catch (EngineException again) when (again.Code == "check")
            {
                await File.WriteAllTextAsync(Path.Combine(work, Rejected + "2.txt"), code, cancellationToken).ConfigureAwait(false);
                throw new VideoException("The video code did not pass the safety check twice and was not run: " + string.Join(" · ", again.Findings.Take(4)))
                {
                    Code = "code",
                };
            }
        }

        steps.Finish("code");

        // F. render (+ one repair with the error text)
        var engineProgress = new Progress<EngineEvent>(e => steps.Report("render", e.Ratio ?? 0, e.Message));
        try
        {
            await engine.RenderAsync(request, engineProgress, cancellationToken).ConfigureAwait(false);
        }
        catch (EngineException ex) when (ex.Code == "render" && !repaired)
        {
            logger.LogWarning("Render failed; asking for one repair: {Error}", ex.Message);
            code = await CodeAsync(chat, codeModel, system, VideoCodePrompt.Repair(code, ex.Message), spent, id, cancellationToken).ConfigureAwait(false);
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

        steps.Finish("render");
        return new RenderOutcome(request, repaired);
    }

    /// <summary>
    /// F without a model: the kept <paramref name="code"/> is written with the new <paramref name="input"/> into <c>&lt;work&gt;/project</c>
    /// (whatever is already there, the scene images, stays) and rendered once. A failure is not repaired: a repair would be a paid call.
    /// </summary>
    public async Task<RenderOutcome> RenderKeptAsync(IVideoEngine engine, string code, RenderInput input, string work, VideoStepProgress steps,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(steps);
        var request = new EngineRenderRequest(Path.Combine(work, "project"), code, input, Path.Combine(work, "silent.mp4"), Workers());
        try
        {
            engine.WriteProject(request);
            await engine.RenderAsync(request, new Progress<EngineEvent>(e => steps.Report("render", e.Ratio ?? 0, e.Message)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (EngineException ex)
        {
            throw new VideoException("Render of the kept code failed: " + ex.Message, ex) { Code = ex.Code == "check" ? "code" : "render" };
        }

        steps.Finish("render");
        return new RenderOutcome(request, false);
    }

    /// <summary>
    /// H + I: subtitles from the input's captions (burned in when chosen), the clips joined with the silent video into the work folder, then
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
        var cues = SubtitleBuilder.Build(cueSources);
        string? ass = null;
        if (save.Options.Subtitles && cues.Count > 0)
        {
            ass = "subtitles.ass";
            await File.WriteAllTextAsync(Path.Combine(save.Work, ass), SubtitleBuilder.Ass(cues, input.Width, input.Height, input.Theme), cancellationToken)
                .ConfigureAwait(false);
        }

        var audio = clips.Select((c, i) => (Clip: c, Start: (long)Math.Round(input.Scenes[i].StartSeconds * 1000))).Where(x => x.Clip is not null)
            .Select(x => (x.Clip!.File, x.Start)).ToList();
        var muxed = Path.Combine(save.Work, "video.partial.mp4");
        steps.Report("mux", 0.1);
        try
        {
            await media.MuxAsync(save.Work, save.Render.Request.Output, audio, ass, muxed, cancellationToken).ConfigureAwait(false);
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
        if (save.Script is { } script)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, VideoFolders.ScriptFile), JsonSerializer.Serialize(script.ToList(), CoreJsonContext.Default.ListString),
                cancellationToken).ConfigureAwait(false);
        }

        var output = Path.Combine(folder, VideoFolders.VideoFile);
        File.Move(muxed, output, overwrite: true);
        steps.Finish("mux");

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
        steps.Finish("save");
        return record;
    }

    /// <summary>
    /// Runs the body of a video run with the failure handling every run shares: a failure or a cancel removes what the run wrote
    /// (<see cref="CleanFailed"/>), keeps what it paid as a run (<see cref="KeepLostAsync"/>, under <paramref name="payer"/>'s provider) and
    /// goes up as a <see cref="VideoException"/> with its code and what was spent (a cancel goes up as it is).
    /// </summary>
    public async Task<VideoRecord> GuardAsync(Guid profileId, ResolvedModel? payer, string work, VersionTarget target, List<VideoCost> spent,
        System.Diagnostics.Stopwatch watch, Func<Task<VideoRecord>> body)
    {
        ArgumentNullException.ThrowIfNull(spent);
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (VideoException ex)
        {
            logger.LogError("Video failed ({Code}): {Message}", ex.Code, ex.Message);
            CleanFailed(work, target);
            await KeepLostAsync(profileId, payer, spent, ex.Code, RunStatus.Failed, watch.Elapsed).ConfigureAwait(false);
            throw new VideoException(ex.Message, ex) { Code = ex.Code, SpentUsd = spent.Sum(c => c.Usd) };
        }
        catch (Providers.ProviderException ex)
        {
            logger.LogError("Video failed: provider error ({Code}, request {RequestId}) after {Usd} USD", ex.Code, ex.RequestId.Length > 0 ? ex.RequestId : "-",
                spent.Sum(c => c.Usd));
            CleanFailed(work, target);
            await KeepLostAsync(profileId, payer, spent, VideoException.ProviderPrefix + ex.Code, RunStatus.Failed, watch.Elapsed).ConfigureAwait(false);
            throw new VideoException(ex.Message, ex) { Code = VideoException.ProviderPrefix + ex.Code, SpentUsd = spent.Sum(c => c.Usd) };
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Video canceled after {Usd} USD were spent", spent.Sum(c => c.Usd));
            CleanFailed(work, target);
            await KeepLostAsync(profileId, payer, spent, "canceled", RunStatus.Canceled, watch.Elapsed).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Video failed: a file could not be written");
            CleanFailed(work, target);
            await KeepLostAsync(profileId, payer, spent, "files", RunStatus.Failed, watch.Elapsed).ConfigureAwait(false);
            throw new VideoException("A file could not be written: " + ex.Message, ex) { Code = "files", SpentUsd = spent.Sum(c => c.Usd) };
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
        // the engine folder) but is skipped all the same.
        foreach (var folder in Directory.EnumerateDirectories(from).Where(d => Path.GetFileName(d) is not ("tmp" or "node_modules")))
        {
            CopyProject(folder, Path.Combine(to, Path.GetFileName(folder)));
        }
    }

    private async Task<string> CodeAsync(IChatClient chat, ResolvedModel codeModel, string system, string user, List<VideoCost> spent, Guid id,
        CancellationToken cancellationToken)
    {
        ChatResponse answer;
        try
        {
            answer = await chat.SendAsync(new ChatRequest(system, user, CacheSystem: true, MaxOutputTokens: VideoCodeBudget.For(codeModel.Model),
                Effort: codeModel.Effort, Stream: true), cancellationToken).ConfigureAwait(false);
        }
        catch (Providers.ProviderException ex) when (ex.ChargedUsage is { } charged)
        {
            // A cut-off or refused code answer is billed: counted before the failure goes up.
            spent.Add(new VideoCost { VideoId = id, Step = PipelineSteps.VideoCode, Usd = costs.Exact(codeModel.Model, charged), Exact = true });
            throw;
        }

        spent.Add(new VideoCost
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
