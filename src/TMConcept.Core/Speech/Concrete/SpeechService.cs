using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;

namespace TMConcept.Core.Speech;

/// <summary>
/// Picks the engine of a profile's choice (local by code; "online" = the provider of the model chosen for the speech step) and keeps a
/// cache: the key is the SHA-256 of text, engine, model, voice and speed, so a repeated scene is not spoken (or paid) twice.
/// </summary>
public sealed class SpeechService(IEnumerable<ISpeechEngine> engines, StepModelResolver resolver, IAppPaths paths, ILogger<SpeechService> logger)
{
    /// <summary>Every engine.</summary>
    public IReadOnlyList<ISpeechEngine> Engines { get; } = engines.ToList();

    /// <summary>The engine of a choice and, for "online", the resolved model.</summary>
    public async Task<(ISpeechEngine Engine, ResolvedModel? Online)> ResolveAsync(string choice, CancellationToken cancellationToken)
    {
        if (choice == SpeechEngines.Online)
        {
            ResolvedModel online;
            try
            {
                online = await resolver.ResolveAsync(PipelineSteps.Speech, null, cancellationToken).ConfigureAwait(false);
            }
            catch (ModelNotReadyException ex)
            {
                throw new SpeechException("No online speech model is ready: " + ex.Message, ex) { Code = "model" };
            }

            var engine = Engines.FirstOrDefault(e => !e.IsLocal && e.Code == online.Model.Provider)
                ?? throw new SpeechException($"{online.Model.Provider} has no speech engine.") { Code = "model" };
            return (engine, online);
        }

        var local = Engines.FirstOrDefault(e => e.IsLocal && e.Code == choice) ?? throw new SpeechException($"Unknown voice engine '{choice}'.") { Code = "missing" };
        return local.Ready ? (local, null) : throw new SpeechException($"{local.Name} is not installed. Install it in Settings → Tools.") { Code = "missing" };
    }

    /// <summary>Speaks into <paramref name="outputWav"/> (from the cache when the same text was spoken before).</summary>
    public async Task<SpeechClip> SynthesizeAsync(string choice, string voice, double speed, string language, string text, string outputWav,
        CancellationToken cancellationToken)
    {
        var (engine, online) = await ResolveAsync(choice, cancellationToken).ConfigureAwait(false);
        var cache = Path.Combine(paths.WorkDirectory, "speech-cache", Key(text, engine.Code, online?.Model.Id ?? string.Empty, voice, speed) + ".wav");
        Directory.CreateDirectory(Path.GetDirectoryName(outputWav)!);
        if (File.Exists(cache))
        {
            File.Copy(cache, outputWav, overwrite: true);
            return new SpeechClip(outputWav, WaveFile.DurationMs(outputWav), text.Length, 0, engine.Code);
        }

        var clip = await engine.SynthesizeAsync(new SpeechRequest(text, language, voice, speed, outputWav, online), cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.Copy(outputWav, cache, overwrite: true);
        logger.LogInformation("Spoke {Characters} characters with {Engine} ({Ms} ms)", clip.Characters, engine.Code, clip.DurationMs);
        return clip;
    }

    /// <summary>The cache key.</summary>
    public static string Key(string text, string engine, string model, string voice, double speed) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', text, engine, model, voice,
            speed.ToString("0.00", CultureInfo.InvariantCulture)))));
}
