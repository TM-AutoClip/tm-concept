using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>
/// Supertonic 3 through sherpa-onnx (the default voice of every output language but English): 31 languages (<see cref="Languages"/>, every
/// output language of the app among them), ten built-in voices, 24 kHz, on the processor; the output language goes to the model as its code. Its weights carry OpenRAIL-M use restrictions; one of them requires saying that the content is machine-generated, so the
/// video details show "AI voice — say so when publishing".
/// </summary>
public sealed class SupertonicSpeechEngine(SpeechModelInstaller installer, IToolLocator tools, SpeechWorkerClient worker,
    ILogger<SupertonicSpeechEngine> logger)
    : SherpaSpeechEngineBase(installer, tools, worker, SpeechModels.Supertonic, logger)
{
    /// <summary>The language codes Supertonic 3 speaks (its model card, 29 Apr 2026).</summary>
    public static readonly IReadOnlySet<string> Languages = new HashSet<string>(StringComparer.Ordinal)
    {
        "en", "ko", "ja", "ar", "bg", "cs", "da", "de", "el", "es", "et", "fi", "fr", "hi", "hr", "hu", "id", "it", "lt", "lv", "nl", "pl", "pt", "ro",
        "ru", "sk", "sl", "sv", "tr", "uk", "vi",
    };

    /// <inheritdoc />
    public override string Code => SpeechEngines.Supertonic;

    /// <inheritdoc />
    public override string Name => "Supertonic 3";

    /// <inheritdoc />
    public override bool Supports(string language) => Languages.Contains(language);

    /// <inheritdoc />
    public override IReadOnlyList<SpeechVoice> Voices(string language) =>
        Enumerable.Range(0, 10).Select(i => new SpeechVoice(i.ToString(CultureInfo.InvariantCulture), (i < 5 ? "M" : "F") + ((i % 5) + 1).ToString(CultureInfo.InvariantCulture))).ToList();

    /// <inheritdoc />
    protected override SpeechWorkerRequest Request(SpeechRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var speaker = int.TryParse(request.Voice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid) ? Math.Clamp(sid, 0, 9) : 0;
        var speed = (float)Math.Clamp(request.Speed, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax);
        return new SpeechWorkerRequest(request.Text, Languages.Contains(request.Language) ? request.Language : ContentLanguages.English, speaker, speed, 8,
            Path.GetFullPath(request.OutputWav));
    }
}
