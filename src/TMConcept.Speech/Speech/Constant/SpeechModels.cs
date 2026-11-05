using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>
/// Pinned voice models (sherpa-onnx tts-models release; hashes from the release). Supertonic 3: weights under OpenRAIL-M (use restrictions
/// passed on to users), code MIT. Piper Turkish dfki: voice data CC BY-NC-SA 4.0 (non-commercial only); not bundled, downloaded only when the
/// user chooses it.
/// </summary>
public static class SpeechModels
{
    /// <summary>Supertonic 3 int8.</summary>
    public static readonly ToolArchive Supertonic = new(ToolKind.SupertonicVoice, "3-2026-05-11", "any",
        new Uri("https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/sherpa-onnx-supertonic-3-tts-int8-2026-05-11.tar.bz2"),
        "82fa96f91c4ef8abaae3a14a3f4153facf88bed821d1f7331cec2700f432c427", "sherpa-onnx-supertonic-3-tts-int8-2026-05-11", "tts.json", 128_774_318);

    /// <summary>Piper Turkish dfki medium int8.</summary>
    public static readonly ToolArchive PiperTurkish = new(ToolKind.PiperTurkishVoice, "dfki-medium-int8", "any",
        new Uri("https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-piper-tr_TR-dfki-medium-int8.tar.bz2"),
        "6cda7bc029b7d5549a80439c51beb4c40c42df4b4614f01a8c61deb2fc859aa2", // SHA-256 of the archive
        "vits-piper-tr_TR-dfki-medium-int8", "tokens.txt", 21_135_582);
}
