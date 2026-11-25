using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;

namespace TMConcept.Core.Music;

/// <inheritdoc cref="IMusicMaker" />
public sealed class MusicMaker(IMusicGenerators generators, IMusicAudio audio, IMyMusic collection, IAppPaths paths, ILogger<MusicMaker> logger,
    IGpuCoordinator? gpu = null) : IMusicMaker
{
    /// <summary>The work folder of the page's pieces (under the app's work folder; removed after each piece).</summary>
    public const string WorkFolder = "music-make";

    /// <inheritdoc />
    public (decimal Usd, int Seconds) Estimate(CatalogModel model, int seconds)
    {
        ArgumentNullException.ThrowIfNull(model);
        var asked = MusicPricing.Seconds(model, seconds);
        return (MusicPricing.Usd(model, asked), asked);
    }

    /// <summary>The prompt sent to the model: the user's words, the measured tempo and, without words, the instrumental note.</summary>
    public static string Prompt(string prompt, int? bpm, bool instrumental)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var text = prompt.Trim()
            + (bpm is { } b ? string.Create(CultureInfo.InvariantCulture, $", about {b} BPM") : string.Empty)
            + (instrumental ? ", instrumental only, no vocals, no singing" : string.Empty);
        return text.Length > MusicGenerationRules.MaxPromptCharacters ? text[..MusicGenerationRules.MaxPromptCharacters] : text;
    }

    /// <summary>The name of a piece without one: the prompt's first words.</summary>
    public static string TitleOf(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8);
        var title = string.Join(' ', words);
        return title.Length > 60 ? title[..60] : title;
    }

    /// <inheritdoc />
    public async Task<MyMusicEntry> MakeAsync(MusicMakeRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new MusicGenerationException("Write what the music should be like.") { Code = "prompt" };
        }

        var generator = generators.For(request.Model) is { } found && generators.Ready(request.Model)
            ? found
            : throw new MusicGenerationException("The music model is not installed: " + request.Model.Name) { Code = "missing" };
        ResolvedModel resolved;
        try
        {
            resolved = await generators.ResolveAsync(request.Model, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelNotReadyException ex)
        {
            throw new MusicGenerationException(ex.Message, ex) { Code = "key" };
        }

        var folder = Path.Combine(paths.WorkDirectory, WorkFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            int? bpm = null;
            var referenceName = string.Empty;
            if (request.ReferenceFile is { Length: > 0 } reference)
            {
                progress?.Report("reference");
                referenceName = Path.GetFileName(reference);
                bpm = await TempoAsync(reference, folder, cancellationToken).ConfigureAwait(false);
            }

            var (_, seconds) = Estimate(request.Model, request.Seconds);
            var prompt = Prompt(request.Prompt, bpm, request.Instrumental);
            var output = Path.Combine(folder, "out");
            var seed = Random.Shared.Next(1, int.MaxValue);
            logger.LogInformation("Music page: {Model}, {Seconds} s, {Bpm} bpm, {Words}, seed {Seed}", ModelBase.KeyOf(request.Model), seconds,
                bpm?.ToString(CultureInfo.InvariantCulture) ?? "-", request.Instrumental ? "instrumental" : "with words", seed);
            progress?.Report("generate");
            var piece = new MusicGenerationRequest(resolved, prompt, seconds, bpm, request.Instrumental, output, seed);
            GeneratedMusic made;
            if (request.Model.Provider == ModelProviders.Local && gpu is not null)
            {
                // One local model on the graphics card at a time (rule 18).
                await using var lease = await gpu.AcquireAsync(request.Model, cancellationToken).ConfigureAwait(false);
                made = await generator.GenerateAsync(piece, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                made = await generator.GenerateAsync(piece, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report("save");
            var title = request.Title.Trim().Length > 0 ? request.Title.Trim() : TitleOf(request.Prompt);
            return await collection.AddGeneratedAsync(made.File, title, ModelBase.KeyOf(request.Model), request.Prompt.Trim(), referenceName, made.Usd,
                bpm ?? 0, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Music page: the work folder of a piece could not be removed");
            }
        }
    }

    /// <summary>The reference sound's tempo (beats per minute), or null when it has no beat the app can find.</summary>
    private async Task<int?> TempoAsync(string file, string work, CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        {
            throw new MusicGenerationException("The reference sound was not found: " + Path.GetFileName(file)) { Code = "reference" };
        }

        var samples = await audio.ReadMonoAsync(file, MusicLibraryRules.SampleRate, MusicLibraryRules.MaxAnalysisSeconds, work, cancellationToken)
            .ConfigureAwait(false) ?? throw new MusicGenerationException("The reference sound could not be read: " + Path.GetFileName(file)) { Code = "reference" };
        var grid = await Task.Run(() => BeatTracker.Analyze(samples, MusicLibraryRules.SampleRate), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Music page: the reference sound's tempo is {Bpm}", grid is null ? "unknown" : grid.Bpm.ToString("0.0", CultureInfo.InvariantCulture));
        return grid is null ? null : (int)Math.Round(grid.Bpm);
    }
}
