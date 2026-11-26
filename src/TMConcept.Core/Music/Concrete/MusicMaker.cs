using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Runs;

namespace TMConcept.Core.Music;

/// <inheritdoc cref="IMusicMaker" />
/// <remarks>
/// The piece itself is made by the shared <see cref="IMusicPieceRunner"/> (the graphics card's lock, every failure as a
/// <see cref="MusicGenerationException"/>), the same as a video's music. A piece of an online model is a run of its own
/// (<see cref="RunKind.Music"/>), so the month's spending counts it with its exact or estimated price.
/// </remarks>
public sealed class MusicMaker(IMusicGenerators generators, IMusicPieceRunner pieces, IMusicAudio audio, IMyMusic collection, IRunStore runs, IAppPaths paths,
    TimeProvider clock, ILogger<MusicMaker> logger) : IMusicMaker
{
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
        var words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MusicGenerationRules.TitleWords);
        var title = string.Join(' ', words);
        return title.Length > MusicGenerationRules.TitleCharacters ? title[..MusicGenerationRules.TitleCharacters] : title;
    }

    /// <inheritdoc />
    public async Task<MyMusicEntry> MakeAsync(MusicMakeRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new MusicGenerationException("Write what the music should be like.") { Code = MusicFailure.Prompt };
        }

        var generator = generators.For(request.Model) is { } found && generators.Ready(request.Model)
            ? found
            : throw new MusicGenerationException("The music model is not installed: " + request.Model.Name) { Code = MusicFailure.Missing };
        ResolvedModel resolved;
        try
        {
            resolved = await generators.ResolveAsync(request.Model, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelNotReadyException ex)
        {
            throw new MusicGenerationException(ex.Message, ex) { Code = MusicFailure.Key };
        }

        var folder = Path.Combine(paths.WorkDirectory, MusicGenerationRules.MakeWorkFolder, Guid.NewGuid().ToString("N"));
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

            var (usd, seconds) = Estimate(request.Model, request.Seconds);
            var prompt = Prompt(request.Prompt, bpm, request.Instrumental);
            var output = Path.Combine(folder, "out");

            // A new seed for every press: "Üret" again with the same words makes another piece (the seed is in the log to make it again).
            var seed = Random.Shared.Next(1, int.MaxValue);
            logger.LogInformation("Music page: {Model}, {Seconds} s, {Bpm} bpm, {Words}, seed {Seed}", ModelBase.KeyOf(request.Model), seconds,
                bpm?.ToString(CultureInfo.InvariantCulture) ?? "-", request.Instrumental ? "instrumental" : "with words", seed);
            progress?.Report("generate");
            var piece = new MusicGenerationRequest(resolved, prompt, seconds, bpm, request.Instrumental, output, seed);
            var made = await GenerateAsync(generator, piece, request.Prompt.Trim(), usd, cancellationToken).ConfigureAwait(false);
            progress?.Report("save");
            var title = request.Title.Trim().Length > 0 ? request.Title.Trim() : TitleOf(request.Prompt);
            return await collection.AddGeneratedAsync(new GeneratedPiece(made.File, title, ModelBase.KeyOf(request.Model), request.Prompt.Trim(), referenceName,
                made.Usd, bpm ?? 0), cancellationToken).ConfigureAwait(false);
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

    /// <summary>Makes the piece; an online model's piece is kept as a run (succeeded with its price, failed, or canceled with its estimate).</summary>
    private async Task<GeneratedMusic> GenerateAsync(IMusicGenerator generator, MusicGenerationRequest piece, string prompt, decimal estimate,
        CancellationToken cancellationToken)
    {
        var model = piece.Model.Model;
        if (model.Provider == ModelProviders.Local)
        {
            return await pieces.RunAsync(generator, piece, cancellationToken).ConfigureAwait(false);
        }

        var run = new Run
        {
            Kind = RunKind.Music,
            Provider = model.Provider,
            Model = model.Id,
            KeyLabel = piece.Model.Key.Label,
            Requested = 1,
            ExtraPrompt = prompt,
            EstimateMinUsd = estimate,
            EstimateMaxUsd = estimate,
            StartedAtUtc = clock.GetUtcNow(),
        };
        await runs.AddAsync(run, CancellationToken.None).ConfigureAwait(false);
        try
        {
            var made = await pieces.RunAsync(generator, piece, cancellationToken).ConfigureAwait(false);
            run.Accepted = 1;
            run.ActualUsd = made.Usd;
            run.CostExact = made.Exact;
            run.Status = RunStatus.Succeeded;
            return made;
        }
        catch (OperationCanceledException)
        {
            // The request may already be billed: counted as the estimate, so the month's spending does not miss it.
            run.ActualUsd = estimate;
            run.CostExact = false;
            run.Status = RunStatus.Canceled;
            run.Error = "canceled";
            throw;
        }
        catch (MusicGenerationException ex)
        {
            run.Status = RunStatus.Failed;
            run.Error = ex.Code.ToString();
            throw;
        }
        finally
        {
            run.FinishedAtUtc = clock.GetUtcNow();
            await runs.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>The reference sound's tempo (beats per minute), or null when it has no beat the app can find.</summary>
    private async Task<int?> TempoAsync(string file, string work, CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        {
            throw new MusicGenerationException("The reference sound was not found: " + Path.GetFileName(file)) { Code = MusicFailure.Reference };
        }

        var samples = await audio.ReadMonoAsync(file, MusicLibraryRules.SampleRate, MusicLibraryRules.MaxAnalysisSeconds, work, cancellationToken)
            .ConfigureAwait(false) ?? throw new MusicGenerationException("The reference sound could not be read: " + Path.GetFileName(file)) { Code = MusicFailure.Reference };
        var grid = await Task.Run(() => BeatTracker.Analyze(samples, MusicLibraryRules.SampleRate), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Music page: the reference sound's tempo is {Bpm}", grid is null ? "unknown" : grid.Bpm.ToString("0.0", CultureInfo.InvariantCulture));
        return grid is null ? null : (int)Math.Round(grid.Bpm);
    }
}
