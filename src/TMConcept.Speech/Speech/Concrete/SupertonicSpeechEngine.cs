using System.Globalization;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;

namespace TMConcept.Speech.Speech;

/// <summary>
/// Supertonic 3 through sherpa-onnx (the default voice): multilingual (Turkish and English among them), ten built-in voices, 24 kHz, on the
/// processor. Its weights carry OpenRAIL-M use restrictions; one of them requires saying that the content is machine-generated, so the
/// video details show "AI voice — say so when publishing".
/// </summary>
public sealed class SupertonicSpeechEngine(SpeechModelInstaller installer, ILogger<SupertonicSpeechEngine> logger)
    : SherpaSpeechEngineBase(installer, SpeechModels.Supertonic, logger)
{
    /// <inheritdoc />
    public override string Code => SpeechEngines.Supertonic;

    /// <inheritdoc />
    public override string Name => "Supertonic 3";

    /// <inheritdoc />
    public override bool Supports(string language) => language is ContentLanguages.Turkish or ContentLanguages.English;

    /// <inheritdoc />
    public override IReadOnlyList<SpeechVoice> Voices(string language) =>
        Enumerable.Range(0, 10).Select(i => new SpeechVoice(i.ToString(CultureInfo.InvariantCulture), (i < 5 ? "M" : "F") + ((i % 5) + 1).ToString(CultureInfo.InvariantCulture))).ToList();

    /// <inheritdoc />
    protected override OfflineTtsConfig Config()
    {
        var config = new OfflineTtsConfig();
        config.Model.Supertonic.DurationPredictor = Path.Combine(Folder, "duration_predictor.int8.onnx");
        config.Model.Supertonic.TextEncoder = Path.Combine(Folder, "text_encoder.int8.onnx");
        config.Model.Supertonic.VectorEstimator = Path.Combine(Folder, "vector_estimator.int8.onnx");
        config.Model.Supertonic.Vocoder = Path.Combine(Folder, "vocoder.int8.onnx");
        config.Model.Supertonic.TtsJson = Path.Combine(Folder, "tts.json");
        config.Model.Supertonic.UnicodeIndexer = Path.Combine(Folder, "unicode_indexer.bin");
        config.Model.Supertonic.VoiceStyle = Path.Combine(Folder, "voice.bin");
        config.Model.NumThreads = Threads;
        config.Model.Debug = 0;
        config.Model.Provider = "cpu";
        return config;
    }

    /// <inheritdoc />
    protected override OfflineTtsGenerationConfig Generation(SpeechRequest request)
    {
        var generation = new OfflineTtsGenerationConfig
        {
            Sid = int.TryParse(request.Voice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid) ? Math.Clamp(sid, 0, 9) : 0,
            NumSteps = 8,
            Speed = (float)Math.Clamp(request.Speed, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax),
        };
        generation.Extra["lang"] = request.Language == ContentLanguages.Turkish ? "tr" : "en";
        return generation;
    }
}
