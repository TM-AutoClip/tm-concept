using Microsoft.Extensions.Logging;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Paths;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;

namespace TMConcept.Core.Videos;

/// <summary>
/// The background music of a run (0.1.44 C3, 0.1.45 C4 + F7 + F8). <see cref="PrepareAsync"/> runs before anything is paid for: the user's own
/// file or a picked library / Openverse track is copied into the work folder and read by ffprobe; a library chosen by the concept is only
/// checked to be installed. <see cref="SettleAsync"/> runs once the scenes are timed (after the scene plan when the concept chooses): the
/// library track is chosen from the plan's music hints (<see cref="MusicMatcher"/>), the track is read by ffmpeg and measured (beat grid,
/// loudness), the scene changes inside the music move onto its beats (<see cref="BeatAligner"/>), the track's entry point and loop are laid out
/// (<see cref="MusicBedPlanner"/>) and written by ffmpeg. A track that cannot be measured or laid out plays as it is (logged); a missing,
/// damaged or silent file, or a library that is not installed, stops the video with <c>music</c> (rule 17).
/// 0.1.46 (C5): generated music is checked in <see cref="PrepareAsync"/> (its model known, installed, with a key) and made in
/// <see cref="SettleAsync"/> — after the narration, with the scene plan's music hints in its prompt — by the local model under the graphics
/// card lock (one local model at a time, with the memory rules of <see cref="IGpuCoordinator"/>) or by an online model, whose price is spent
/// as the video's "music" cost line. A piece shorter than the video is looped (F8); no second piece is made. A failure stops the video with
/// <c>music</c> (the queue's night mode then goes on with the next video). A new version uses the piece kept with the version: the music is
/// never made again for another voice or engine (rule 12).
/// </summary>
public sealed class VideoMusicStep(IMediaTools media, IMusicLibrary library, IMusicAudio audio, ILogger<VideoMusicStep> logger,
    IMusicGenerators generators, IMusicPieceRunner pieces)
{
    /// <summary>The laid-out track's name in the work folder's music folder.</summary>
    public const string BedName = "bed.flac";

    /// <summary>
    /// The template's background music for a run, checked before anything is paid for: off = <c>null</c>. A new version uses the copy kept in
    /// <paramref name="keptVersion"/>'s <see cref="VideoFolders.Music"/> folder with its asset record line, so it sounds the same even when the
    /// original was moved or the library removed.
    /// </summary>
    public async Task<PreparedMusic?> PrepareAsync(VideoStyle? style, string? keptVersion, string work, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(work);
        if (!VideoStyleRules.MusicOn(style))
        {
            return null;
        }

        var music = style!.Music;
        if (music.Source is not (MusicSource.OwnFile or MusicSource.Library or MusicSource.GenerateLocal or MusicSource.GenerateOnline or MusicSource.MyMusic))
        {
            logger.LogError(Activity.LogEvents.OnScreen, "The template's music source {Source} cannot be made by this version; the video is stopped", music.Source);
            throw new VideoException($"The music source {music.Source} is not available in this version.") { Code = "music" };
        }

        if ((keptVersion is null ? null : VideoAssembler.KeptMusic(keptVersion)) is { } kept)
        {
            var keptEntry = (await AssetLedger.ReadAsync(keptVersion!, cancellationToken).ConfigureAwait(false)).FirstOrDefault(e => e.Kind == AssetKinds.Music);
            return await CopyAsync(kept, keptEntry ?? OwnEntry(music, kept), "kept with the video", work, cancellationToken).ConfigureAwait(false);
        }

        if (music.Source == MusicSource.OwnFile)
        {
            return await CopyAsync(music.File, OwnEntry(music, music.File), "the template's file", work, cancellationToken).ConfigureAwait(false);
        }

        if (music.Source is MusicSource.GenerateLocal or MusicSource.GenerateOnline)
        {
            return await GeneratorAsync(music, cancellationToken).ConfigureAwait(false);
        }

        // 0.1.54: a piece of the user's own collection ("Müziklerim": made on the music page or added there).
        if (music.Source == MusicSource.MyMusic)
        {
            if (music.Track is not { } mine)
            {
                logger.LogError(Activity.LogEvents.OnScreen, "No piece of My music is chosen; the video is stopped");
                throw new VideoException("No piece of My music is chosen.") { Code = "music" };
            }

            return await CopyAsync(mine.File, TrackEntry(mine, mine.File), "my music", work, cancellationToken).ConfigureAwait(false);
        }

        if (music.Track is { } picked)
        {
            var file = picked.Id.StartsWith(MusicLibraryRules.OpenversePrefix, StringComparison.Ordinal) ? picked.File : library.FileOf(picked);
            if (file is null)
            {
                logger.LogError(Activity.LogEvents.OnScreen, "The template's library track {Track} is not on this computer (the music library is not installed); the video is stopped",
                    picked.Title);
                throw new VideoException("The music library is not installed: " + picked.Title) { Code = "music" };
            }

            return await CopyAsync(file, TrackEntry(picked, file), "the template's track", work, cancellationToken).ConfigureAwait(false);
        }

        if (!library.Ready)
        {
            logger.LogError(Activity.LogEvents.OnScreen, "The template's music comes from the library, which is not installed; the video is stopped");
            throw new VideoException("The music library is not installed.") { Code = "music" };
        }

        logger.LogInformation("Background music: a library track chosen by the concept");
        return new PreparedMusic(string.Empty, 0, new AssetEntry { Kind = AssetKinds.Music }) { Automatic = true };
    }

    /// <summary>
    /// Lays the music under the timed <paramref name="input"/> (and, with <paramref name="align"/>, moves its scene changes onto the beats; the
    /// scene plans' beats are timed again). Returns the music to mix; <paramref name="music"/> null = null.
    /// </summary>
    /// <param name="spend">Gets what generated music cost (its USD and whether that is exact) as soon as it is made (0.1.46).</param>
    /// <param name="progress">Gets how far the music model is while it makes the piece (0.1.61; the video's step row shows it).</param>
    public async Task<PreparedMusic?> SettleAsync(PreparedMusic? music, VideoStyle? style, RenderInput input, IReadOnlyList<SpeechClip?> clips, string work,
        bool align, CancellationToken cancellationToken, Action<decimal, bool>? spend = null, IProgress<MusicProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(clips);
        if (music is null || input.Scenes.Count == 0)
        {
            return music;
        }

        var hints = input.Scenes.Select(s => s.Plan?.Music).ToList();
        var tempo = style?.Tempo ?? NarrationTempos.Default;
        if (music.Generate is { } generate)
        {
            music = await GenerateAsync(generate, style?.Music ?? new VideoMusic(), hints, tempo, input.DurationSeconds, work, spend, progress,
                cancellationToken).ConfigureAwait(false);
        }
        else if (music.Automatic)
        {
            music = await ChooseAsync(hints, tempo, work, cancellationToken).ConfigureAwait(false);
        }

        var window = MusicWindows.Of(hints, input.Scenes.Count);
        var samples = await ReadAsync(music.File, work, cancellationToken).ConfigureAwait(false);
        if (samples is null)
        {
            logger.LogWarning("The music could not be measured; it plays from its start, looped when short");
            return music with { Placement = Place(input, window, null, 0) };
        }

        var (grid, energy) = await Task.Run(() => (BeatTracker.Analyze(samples, MusicLibraryRules.SampleRate),
            MusicBedPlanner.Measure(samples, MusicLibraryRules.SampleRate)), cancellationToken).ConfigureAwait(false);
        var startSeconds = input.Scenes[window.FirstScene].StartSeconds;

        // The user's own file and a generated piece play from their start (only a library or picked track takes its strongest opening; a
        // generated piece was made for this video and opens as its model made it).
        var entry = music.Entry.License == AssetLicenses.OwnFile || music.Entry.License.StartsWith(AssetLicenses.GeneratedPrefix, StringComparison.Ordinal)
            ? 0
            : MusicBedPlanner.Entry(energy, grid, End(input, window) - startSeconds);
        var moved = 0;
        if (align && grid is not null)
        {
            // The first beat after the entry, in the video's time (the entry is on a bar line unless the file plays from its start).
            var offset = ((grid.FirstBeatSeconds - entry) % grid.BeatSeconds + grid.BeatSeconds) % grid.BeatSeconds;
            moved = Align(input, clips, window, grid, startSeconds + offset, tempo);
        }

        Louder(input, energy, startSeconds, entry);
        var need = End(input, window) - startSeconds;
        var tail = MusicBedPlanner.TailEnd(energy);
        if (tail <= entry + MusicLibraryRules.MinCrossfadeSeconds)
        {
            logger.LogWarning("The music has almost no sound after its entry; it plays from its start, looped when short");
            return music with { Placement = Place(input, window, null, 0) };
        }

        var bed = MusicBedPlanner.Plan(entry, need, tail, grid);
        var output = Path.Combine(work, VideoFolders.Music, BedName);
        var made = false;
        try
        {
            made = await audio.RenderBedAsync(music.File, bed, output, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The music's entry and loop could not be written");
        }

        logger.LogInformation("Music: {Name}, {Bpm} bpm (first beat {First:0.000} s), entry {Entry:0.00} s, {Copies} copies with {Fade:0.00} s cross-fades, "
            + "{Moved} scene changes moved onto the beat, scenes {From}-{To}{Laid}", music.Entry.Name, grid?.Bpm ?? 0, grid?.FirstBeatSeconds ?? 0, entry,
            bed.Copies, bed.CrossfadeSeconds, moved, window.FirstScene + 1, window.LastScene + 1, made ? string.Empty : " (played as it is)");
        return music with { Placement = Place(input, window, made ? output : null, made ? bed.TotalSeconds : 0) };
    }

    /// <summary>
    /// The generated music's model, checked before anything is paid for: in the catalog, of the template's source, installed (the local model's
    /// components; never installed here, rule 18) and with a key (an online model). Anything missing stops the video with <c>music</c>.
    /// </summary>
    private async Task<PreparedMusic> GeneratorAsync(VideoMusic music, CancellationToken cancellationToken)
    {
        if (generators.Find(music.Generator) is not { } model)
        {
            logger.LogError(Activity.LogEvents.OnScreen, "The template's music model {Model} is not known; the video is stopped",
                music.Generator.Length > 0 ? music.Generator : "(none)");
            throw new VideoException("No music model is chosen for the generated music.") { Code = "music" };
        }

        if ((model.Provider == ModelProviders.Local) != (music.Source == MusicSource.GenerateLocal))
        {
            logger.LogError(Activity.LogEvents.OnScreen, "The template's music model {Model} does not fit its source {Source}; the video is stopped",
                ModelBase.KeyOf(model), music.Source);
            throw new VideoException("The music model does not fit the music source: " + model.Name) { Code = "music" };
        }

        if (generators.For(model) is null || !generators.Ready(model))
        {
            logger.LogError(Activity.LogEvents.OnScreen, "The music model {Model} is not installed; the video is stopped", model.Name);
            throw new VideoException("The music model is not installed: " + model.Name) { Code = "music" };
        }

        ResolvedModel resolved;
        try
        {
            resolved = await generators.ResolveAsync(model, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelNotReadyException ex)
        {
            logger.LogError(Activity.LogEvents.OnScreen, "The music model {Model} is not ready (no {Missing}); the video is stopped", model.Name, ex.Missing);
            throw new VideoException(ex.Message, ex) { Code = "music" };
        }

        logger.LogInformation("Background music: made by {Model} after the narration", ModelBase.KeyOf(model));
        return new PreparedMusic(string.Empty, 0, new AssetEntry { Kind = AssetKinds.Music }) { Automatic = true, Generate = resolved };
    }

    /// <summary>
    /// Makes the piece with the hints in its prompt (the local model under the graphics card lock), spends its price and copies it into the
    /// work folder like any other track. Any failure stops the video with <c>music</c>.
    /// </summary>
    private async Task<PreparedMusic> GenerateAsync(ResolvedModel model, VideoMusic settings, IReadOnlyList<MusicHint?> hints, NarrationTempo tempo,
        double videoSeconds, string work, Action<decimal, bool>? spend, IProgress<MusicProgress>? progress, CancellationToken cancellationToken)
    {
        var generator = generators.For(model.Model) is { } found
            ? found
            : throw new VideoException("The music model is not installed: " + model.Model.Name) { Code = "music" };
        var seconds = MusicPricing.Seconds(model.Model, videoSeconds);
        var (_, _, pace) = MusicPromptBuilder.Choose(hints, tempo);
        var prompt = MusicPromptBuilder.Build(hints, tempo, settings.Instrumental, seconds);
        var folder = Path.Combine(work, MusicGenerationRules.WorkFolder);
        Directory.CreateDirectory(folder);
        logger.LogInformation("Music generation: {Model}, {Seconds} s, {Pace}, {Words} (prompt: {Prompt})", ModelBase.KeyOf(model.Model), seconds, pace,
            settings.Instrumental ? "instrumental" : "with words", prompt);
        var request = new MusicGenerationRequest(model, prompt, seconds, MusicPromptBuilder.Bpm(pace), settings.Instrumental, folder,
            MusicGenerationRules.DefaultSeed) { Progress = progress };
        GeneratedMusic made;
        try
        {
            // The shared runner: the graphics card's lock for a local model (the narration's worker has ended, an image model is unloaded first).
            made = await pieces.RunAsync(generator, request, cancellationToken).ConfigureAwait(false);
        }
        catch (MusicGenerationException ex)
        {
            // Rule 17: a failure stops the video (shown, logged in red); in night mode the queue goes on with the next one.
            logger.LogError(Activity.LogEvents.OnScreen, ex, "The music could not be made by {Model}; the video is stopped", model.Model.Name);
            throw new VideoException("The music could not be made: " + ex.Message, ex) { Code = ex.Code == MusicFailure.Memory ? "memory" : "music" };
        }

        if (made.Usd > 0)
        {
            spend?.Invoke(made.Usd, made.Exact);
        }

        logger.LogInformation("Music made by {Model} ({Usd} USD{Exact})", model.Model.Name, made.Usd, made.Exact ? string.Empty : ", estimated");
        return await CopyAsync(made.File, GeneratedEntry(model.Model), "made for this video", work, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The spending of generated music as the video's <see cref="Pipeline.PipelineSteps.Music"/> cost line (0.1.46).</summary>
    public static Action<decimal, bool> Spender(VideoStepProgress steps, Guid videoId)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return (usd, exact) => steps.Spend(new VideoCost { VideoId = videoId, Step = Pipeline.PipelineSteps.Music, Usd = usd, Exact = exact });
    }

    /// <summary>The asset record line of generated music (F15): "generated: &lt;model&gt;, &lt;licence&gt;".</summary>
    public static AssetEntry GeneratedEntry(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var license = model.Provider == ModelProviders.Local
            ? (model.License.Length > 0 ? model.License.ToUpperInvariant() : "unknown")
            : model.Provider + " terms of use";
        return new AssetEntry
        {
            Kind = AssetKinds.Music,
            Name = "Generated music",
            Artist = model.Name,
            License = AssetLicenses.GeneratedPrefix + " " + model.Name + ", " + license,
            SourceUrl = model.LicenseUrl,
        };
    }

    /// <summary>The library track for the hints (only installed tracks); stops the video when there is none.</summary>
    private async Task<PreparedMusic> ChooseAsync(IReadOnlyList<MusicHint?> hints, NarrationTempo tempo, string work, CancellationToken cancellationToken)
    {
        var installed = library.Tracks.Where(t => library.FileOf(t) is not null).ToList();
        if (MusicMatcher.Choose(installed, hints, tempo) is not { } choice)
        {
            logger.LogError(Activity.LogEvents.OnScreen, "The music library is not installed; the video is stopped");
            throw new VideoException("The music library is not installed.") { Code = "music" };
        }

        logger.LogInformation("Library music chosen by the concept: {Title} (score {Score}{Nearest}; hints {Hints})", choice.Track.Title, choice.Score,
            choice.Nearest ? ", nearest style" : string.Empty, hints.Count(h => h is not null));
        var file = library.FileOf(choice.Track)!;
        return await CopyAsync(file, TrackEntry(choice.Track, file), "chosen by the concept", work, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves the scene changes inside the music onto its beats; returns how many moved.</summary>
    private static int Align(RenderInput input, IReadOnlyList<SpeechClip?> clips, MusicWindow window, BeatGrid grid, double startSeconds, NarrationTempo tempo)
    {
        var frames = input.Scenes.Select(s => s.FrameCount).ToList();
        var slack = input.Scenes.Select((s, i) => Slack(s, i < clips.Count ? clips[i] : null, input.Fps)).ToList();
        var aligned = BeatAligner.Align(frames, slack, input.Fps, startSeconds, grid, BeatAligner.Tolerance(tempo), window.FirstScene, window.LastScene);
        var moved = aligned.Where((f, i) => f != frames[i]).Count();
        if (moved > 0)
        {
            RenderInputBuilder.Retime(input, aligned);
            Plans.PlanTiming.Carry(input, input, clips.Select(c => c?.Spans).ToList());
        }

        return moved;
    }

    /// <summary>The frames a scene may lose: its clip's silence after the last word, less what stays (none for a silent scene).</summary>
    private static int Slack(RenderScene scene, SpeechClip? clip, int fps)
    {
        if (clip is null)
        {
            return 0;
        }

        long spokenEnd;
        try
        {
            spokenEnd = NarrationSilence.Spoken(clip.File) is { Count: > 0 } spoken ? spoken[^1].EndMs : clip.DurationMs;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            spokenEnd = clip.DurationMs;
        }

        var free = (scene.FrameCount / (double)fps) - (spokenEnd / 1000.0) - MusicLibraryRules.KeepSilenceSeconds;
        return free > 0 ? (int)Math.Floor(free * fps) : 0;
    }

    /// <summary>Scene changes on loud music take a shorter transition (F1).</summary>
    private static void Louder(RenderInput input, MusicEnergy energy, double startSeconds, double entry)
    {
        var factors = BeatAligner.TransitionFactors(input.Scenes.Select(s => s.StartSeconds).ToList(), energy,
            t => t >= startSeconds ? entry + (t - startSeconds) : null);
        for (var i = 0; i < input.Scenes.Count; i++)
        {
            if (factors[i] < 1 && input.Scenes[i].Plan is { TransitionSeconds: > 0 } plan)
            {
                plan.TransitionSeconds = Math.Round(plan.TransitionSeconds * factors[i], 2);
            }
        }
    }

    /// <summary>Where the window ends in the video (the video's end when it ends with the last scene).</summary>
    private static double End(RenderInput input, MusicWindow window) =>
        window.LastScene >= input.Scenes.Count - 1 ? input.DurationSeconds : input.Scenes[window.LastScene].StartSeconds + input.Scenes[window.LastScene].DurationSeconds;

    private static MusicPlacement Place(RenderInput input, MusicWindow window, string? bed, double bedSeconds)
    {
        var start = input.Scenes[window.FirstScene].StartSeconds;
        double? end = window.LastScene >= input.Scenes.Count - 1 ? null : End(input, window);
        var fadeIn = window.FadeIn ? MusicLibraryRules.WindowFadeSeconds : start > 0 ? 0.05 : 0;
        var fadeOut = window.FadeOut ? MusicLibraryRules.WindowFadeSeconds : 0.05;
        return new MusicPlacement(bed, bedSeconds, start, end, fadeIn, fadeOut);
    }

    private async Task<float[]?> ReadAsync(string file, string work, CancellationToken cancellationToken)
    {
        try
        {
            return await audio.ReadMonoAsync(file, MusicLibraryRules.SampleRate, MusicLibraryRules.MaxAnalysisSeconds, Path.Combine(work, VideoFolders.Music),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The music could not be read for its beats");
            return null;
        }
    }

    /// <summary>Copies a track into the work folder and reads its length (a missing, unreadable or silent file stops the video).</summary>
    private async Task<PreparedMusic> CopyAsync(string? from, AssetEntry entry, string origin, string work, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(from) || !File.Exists(from))
        {
            // The file name only: the folder is the user's.
            logger.LogError(Activity.LogEvents.OnScreen, "The music file {File} was not found; the video is stopped", Path.GetFileName(from ?? string.Empty));
            throw new VideoException("The music file was not found: " + Path.GetFileName(from ?? string.Empty)) { Code = "music" };
        }

        var folder = Path.Combine(work, VideoFolders.Music);
        Directory.CreateDirectory(folder);
        var name = LibraryPaths.SafeName(Path.GetFileNameWithoutExtension(from), "music") + Path.GetExtension(from).ToLowerInvariant();
        var copy = Path.Combine(folder, name);
        try
        {
            await LibraryPaths.CopyAsync(from, copy, FileMode.Create, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(Activity.LogEvents.OnScreen, ex, "The music file {File} could not be read; the video is stopped", Path.GetFileName(from));
            throw new VideoException("The music file could not be read: " + Path.GetFileName(from), ex) { Code = "music" };
        }

        var duration = await media.AudioDurationAsync(copy, cancellationToken).ConfigureAwait(false);
        if (duration is not > 0)
        {
            logger.LogError(Activity.LogEvents.OnScreen, "The music file {File} is damaged or has no sound; the video is stopped", Path.GetFileName(from));
            throw new VideoException("The music file is damaged or has no sound: " + Path.GetFileName(from)) { Code = "music" };
        }

        logger.LogInformation("Background music: {File} ({Seconds:0.0} s, {Origin})", name, duration.Value / 1000.0, origin);
        entry.File = name;
        return new PreparedMusic(copy, duration.Value, entry);
    }

    /// <summary>The asset record line of the user's own file (its licence is the user's).</summary>
    private static AssetEntry OwnEntry(VideoMusic music, string? file)
    {
        var attribution = music.Attribution.Trim();
        return new AssetEntry
        {
            Kind = AssetKinds.Music,
            Name = Path.GetFileNameWithoutExtension(file ?? string.Empty),
            License = AssetLicenses.OwnFile,
            Attribution = attribution.Length > MusicMixDefaults.AttributionMax ? attribution[..MusicMixDefaults.AttributionMax] : attribution,
        };
    }

    /// <summary>The asset record line of a library or Openverse track: its source, creator, licence and attribution (F15).</summary>
    public static AssetEntry TrackEntry(MusicTrack track, string file)
    {
        ArgumentNullException.ThrowIfNull(track);
        var attribution = track.Attribution.Trim();
        return new AssetEntry
        {
            Kind = AssetKinds.Music,
            Name = track.Title.Trim().Length > 0 ? track.Title.Trim() : Path.GetFileNameWithoutExtension(file),
            Artist = track.Artist.Trim(),
            SourceUrl = track.PageUrl.Trim(),
            License = track.License.Trim(),
            Attribution = attribution.Length > MusicMixDefaults.AttributionMax ? attribution[..MusicMixDefaults.AttributionMax] : attribution,
        };
    }
}
