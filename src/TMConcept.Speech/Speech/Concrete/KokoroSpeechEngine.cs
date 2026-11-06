using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Text;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>
/// Kokoro 82M through sherpa-onnx: a free local voice of good quality for English (American and British voices; the model has other
/// languages, but without espeak-ng sherpa-onnx 2.x reads only English and Chinese from its dictionaries, so it is offered for English
/// profiles only). 24 kHz, on the processor. Apache-2.0. Numbers are spelled out first (the dictionary has no digits).
/// </summary>
public sealed class KokoroSpeechEngine(SpeechModelInstaller installer, IToolLocator tools, SpeechWorkerClient worker, ILogger<KokoroSpeechEngine> logger)
    : SherpaSpeechEngineBase(installer, tools, worker, SpeechModels.Kokoro, logger)
{
    /// <summary>The English voices of the v1.0 voice table (speaker id, name), the suggested one first.</summary>
    public static readonly IReadOnlyList<(int Id, string Name)> EnglishVoices =
    [
        (3, "af_heart"), (2, "af_bella"), (6, "af_nicole"), (9, "af_sarah"), (0, "af_alloy"), (1, "af_aoede"), (4, "af_jessica"), (5, "af_kore"),
        (7, "af_nova"), (8, "af_river"), (10, "af_sky"), (16, "am_michael"), (14, "am_fenrir"), (17, "am_onyx"), (11, "am_adam"), (12, "am_echo"),
        (13, "am_eric"), (15, "am_liam"), (18, "am_puck"), (21, "bf_emma"), (22, "bf_isabella"), (20, "bf_alice"), (23, "bf_lily"), (26, "bm_george"),
        (25, "bm_fable"), (24, "bm_daniel"), (27, "bm_lewis"),
    ];

    /// <inheritdoc />
    public override string Code => SpeechEngines.Kokoro;

    /// <inheritdoc />
    public override string Name => "Kokoro 82M";

    /// <inheritdoc />
    public override bool Supports(string language) => language == ContentLanguages.English;

    /// <inheritdoc />
    public override IReadOnlyList<SpeechVoice> Voices(string language) =>
        EnglishVoices.Select(v => new SpeechVoice(v.Id.ToString(CultureInfo.InvariantCulture), Label(v.Name))).ToList();

    /// <inheritdoc />
    protected override SpeechWorkerRequest Request(SpeechRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var speaker = int.TryParse(request.Voice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid) && EnglishVoices.Any(v => v.Id == sid)
            ? sid
            : EnglishVoices[0].Id;
        var speed = (float)Math.Clamp(request.Speed, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax);
        return new SpeechWorkerRequest(EnglishNumbers.Spell(request.Text), "en", speaker, speed, 1, Path.GetFullPath(request.OutputWav));
    }

    /// <summary>"af_heart" → "Heart (American, female)".</summary>
    private static string Label(string name)
    {
        var accent = name[0] == 'a' ? "American" : "British";
        var voice = name[1] == 'f' ? "female" : "male";
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name[3..]) + " (" + accent + ", " + voice + ")";
    }
}
