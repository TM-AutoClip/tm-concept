using Microsoft.Extensions.Logging;
using SherpaOnnx;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;

namespace TMConcept.Speech.Speech;

/// <summary>
/// The Piper Turkish voice (dfki) through sherpa-onnx; Piper's GPL program is not used. The voice data is CC BY-NC-SA 4.0: non-commercial
/// use only, and the license carries over to every video made with it (the video details keep a permanent label). Not bundled; downloaded
/// only after the user accepted that warning.
/// </summary>
public sealed class PiperTurkishSpeechEngine(SpeechModelInstaller installer, ILogger<PiperTurkishSpeechEngine> logger)
    : SherpaSpeechEngineBase(installer, SpeechModels.PiperTurkish, logger)
{
    /// <inheritdoc />
    public override string Code => SpeechEngines.PiperTurkish;

    /// <inheritdoc />
    public override string Name => "Piper Türkçe (dfki)";

    /// <inheritdoc />
    public override bool Supports(string language) => language == ContentLanguages.Turkish;

    /// <inheritdoc />
    public override IReadOnlyList<SpeechVoice> Voices(string language) => [new("0", "dfki")];

    /// <inheritdoc />
    protected override OfflineTtsConfig Config()
    {
        var config = new OfflineTtsConfig();
        config.Model.Vits.Model = Path.Combine(Folder, "tr_TR-dfki-medium.int8.onnx");
        config.Model.Vits.Tokens = Path.Combine(Folder, "tokens.txt");
        config.Model.Vits.DataDir = Path.Combine(Folder, "espeak-ng-data");
        config.Model.NumThreads = Threads;
        config.Model.Debug = 0;
        config.Model.Provider = "cpu";
        return config;
    }

    /// <inheritdoc />
    protected override OfflineTtsGenerationConfig Generation(SpeechRequest request) => new()
    {
        Sid = 0,
        Speed = (float)Math.Clamp(request.Speed, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax),
    };
}
