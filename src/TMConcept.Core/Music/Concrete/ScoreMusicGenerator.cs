using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;

namespace TMConcept.Core.Music;

/// <summary>
/// "Program composes" (0.1.55, catalog <c>local/program-score</c>): music from code, the counterpart of the videos drawn from code. The score is
/// written by <see cref="IScoreWriter"/> (the app's own composer: no model, no download of a model, the same prompt and seed give the same
/// piece), checked (<see cref="ScoreValidator"/>), written as a MIDI file (<see cref="MidiWriter"/>) and played into a WAV file by a SoundFont
/// synthesizer on the processor (<see cref="IMidiRenderer"/>). Free, offline, any computer: it never takes the graphics card's lock (its catalog
/// entry names no card). The MIDI file stays next to the piece in the work folder.
/// </summary>
public sealed class ScoreMusicGenerator(IScoreWriter writer, IMidiRenderer renderer, ILogger<ScoreMusicGenerator> logger) : IMusicGenerator
{
    /// <summary>The MIDI file's name in the piece's folder.</summary>
    public const string MidiName = "score.mid";

    /// <summary>The piece's file name.</summary>
    public const string WavName = "score.wav";

    /// <inheritdoc />
    public bool Handles(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local && model.Id == ScoreRules.ModelId;
    }

    /// <inheritdoc />
    public bool Ready(CatalogModel model) => Handles(model) && renderer.Ready;

    /// <inheritdoc />
    public async Task<GeneratedMusic> GenerateAsync(MusicGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!renderer.Ready)
        {
            throw new MusicGenerationException("The synthesizer of the composed music is not installed.") { Code = MusicFailure.Missing };
        }

        var score = writer.Write(new ScoreBrief(request.Prompt, request.Seconds, request.Bpm, request.Seed));
        if (ScoreValidator.Problems(score) is { Count: > 0 } problems)
        {
            throw new MusicGenerationException("The score cannot be played: " + string.Join("; ", problems)) { Code = MusicFailure.Music };
        }

        Directory.CreateDirectory(request.OutputFolder);
        var midi = Path.Combine(request.OutputFolder, MidiName);
        var wav = Path.Combine(request.OutputFolder, WavName);
        await File.WriteAllBytesAsync(midi, MidiWriter.Write(score), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Composed music: {Bpm} bpm, {Bars} bars ({Seconds:0.0} s), {Tracks} tracks, {Notes} notes, seed {Seed}", score.Bpm, score.Bars,
            score.Seconds, score.Tracks.Count, score.Tracks.Sum(t => t.Notes.Count), request.Seed);
        await renderer.RenderAsync(midi, wav, cancellationToken).ConfigureAwait(false);
        return File.Exists(wav) && new FileInfo(wav).Length > 0
            ? new GeneratedMusic(wav, 0, true)
            : throw new MusicGenerationException("The synthesizer wrote no piece.") { Code = MusicFailure.Missing };
    }
}
