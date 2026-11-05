using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>
/// Supertonic 3 through sherpa-onnx (the default voice): multilingual (Turkish and English among them), ten built-in voices, 24 kHz, on the
/// processor. Its weights carry OpenRAIL-M use restrictions; one of them requires saying that the content is machine-generated, so the
/// video details show "AI voice — say so when publishing".
/// </summary>
public sealed class SupertonicSpeechEngine(SpeechModelInstaller installer, IToolLocator tools, SpeechWorkerClient worker,
    ILogger<SupertonicSpeechEngine> logger)
    : SherpaSpeechEngineBase(installer, tools, worker, SpeechModels.Supertonic, logger)
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
    protected override SpeechWorkerRequest Request(SpeechRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var speaker = int.TryParse(request.Voice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid) ? Math.Clamp(sid, 0, 9) : 0;
        var speed = (float)Math.Clamp(request.Speed, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax);
        return new SpeechWorkerRequest(request.Text, request.Language == ContentLanguages.Turkish ? "tr" : "en", speaker, speed, 8,
            Path.GetFullPath(request.OutputWav));
    }
}
