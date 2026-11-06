using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Speech;

/// <summary>
/// The narration step: a local model names its engine by its id ("local/supertonic"), an online model by its provider. The choice comes
/// from the profile's choice made on the video page (off = a silent video). A cache keeps every spoken text: the key is the SHA-256 of
/// text, engine, model, voice and speed, so a repeated scene is not spoken (or paid) twice.
/// </summary>
public sealed class SpeechService(IEnumerable<ISpeechEngine> engines, StepModelResolver resolver, IAppPaths paths, ILogger<SpeechService> logger)
{
    /// <summary>Every engine.</summary>
    public IReadOnlyList<ISpeechEngine> Engines { get; } = engines.ToList();

    /// <summary>The engine of a narration model; null when the app has none for it.</summary>
    public ISpeechEngine? EngineOf(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local
            ? Engines.FirstOrDefault(e => e.IsLocal && e.Code == model.Id)
            : Engines.FirstOrDefault(e => !e.IsLocal && e.Code == model.Provider);
    }

    /// <summary>The narration of a profile (its saved choice) for text in <paramref name="language"/>; null when it is off.</summary>
    public async Task<SpeechSetup?> ResolveAsync(Guid? profileId, string language, CancellationToken cancellationToken)
    {
        var choice = await resolver.ChoiceAsync(PipelineSteps.Speech, profileId, cancellationToken).ConfigureAwait(false);
        return StepModelResolver.IsOff(choice) ? null : await SetupAsync(choice!, language, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The narration of a choice (a page's own one: "Listen") for text in <paramref name="language"/>. A voice that does not speak that
    /// language is refused (<see cref="SpeechException"/>, code "language"): an English-only voice never reads Turkish text, even when an
    /// older choice of the profile still names it.
    /// </summary>
    public async Task<SpeechSetup> SetupAsync(StepModelChoice choice, string language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ResolvedModel model;
        try
        {
            model = await resolver.ResolveAsync(PipelineSteps.Speech, choice, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelNotReadyException ex)
        {
            throw new SpeechException("The narration model is not ready: " + ex.Message, ex) { Code = "model" };
        }

        var engine = EngineOf(model.Model) ?? throw new SpeechException($"{model.Model.Provider}/{model.Model.Id} has no voice engine.") { Code = "model" };
        if (!engine.Supports(language))
        {
            logger.LogWarning("Narration refused: {Engine} does not speak the content language {Language}", engine.Code, language);
            throw new SpeechException($"{engine.Name} does not speak the content language ({language}); choose another voice on the video page.") { Code = "language" };
        }

        if (engine.IsLocal && !engine.Ready)
        {
            throw new SpeechException($"{engine.Name} is not installed. It is installed when a process that uses it starts (after a confirmation).") { Code = "missing" };
        }

        var options = StepModelResolver.Options(choice);
        return new SpeechSetup(engine, model.Model, engine.IsLocal ? null : model, options.Voice,
            Math.Clamp(options.Speed, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax));
    }

    /// <summary>Speaks into <paramref name="outputWav"/> (from the cache when the same text was spoken before).</summary>
    public async Task<SpeechClip> SynthesizeAsync(SpeechSetup setup, string language, string text, string outputWav, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var voice = setup.Voice.Length > 0 ? setup.Voice : setup.Engine.Voices(language).FirstOrDefault()?.Id ?? string.Empty;
        var cache = Path.Combine(paths.WorkDirectory, "speech-cache", Key(text, setup.Engine.Code, setup.Model.Id, voice, setup.Speed) + ".wav");
        Directory.CreateDirectory(Path.GetDirectoryName(outputWav)!);
        if (File.Exists(cache))
        {
            File.Copy(cache, outputWav, overwrite: true);
            return new SpeechClip(outputWav, WaveFile.DurationMs(outputWav), text.Length, 0, setup.Engine.Code);
        }

        var clip = await setup.Engine.SynthesizeAsync(new SpeechRequest(text, language, voice, setup.Speed, outputWav, setup.Online), cancellationToken)
            .ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.Copy(outputWav, cache, overwrite: true);
        logger.LogInformation("Spoke {Characters} characters with {Engine} ({Ms} ms)", clip.Characters, setup.Engine.Code, clip.DurationMs);
        return clip;
    }

    /// <summary>The cache key.</summary>
    public static string Key(string text, string engine, string model, string voice, double speed) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', text, engine, model, voice,
            speed.ToString("0.00", CultureInfo.InvariantCulture)))));
}
