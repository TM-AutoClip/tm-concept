using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>
/// Pinned voice models (sherpa-onnx tts-models release; hashes from the release). Supertonic 3: weights under OpenRAIL-M (use restrictions
/// passed on to users), code MIT. The Piper Turkish voice was removed in 0.1.1: sherpa-onnx 2.x dropped espeak-ng (GPL), so a Piper model
/// can no longer turn text into phonemes by itself, and no Turkish lexicon without espeak is available.
/// </summary>
public static class SpeechModels
{
    /// <summary>Supertonic 3 int8.</summary>
    public static readonly ToolArchive Supertonic = new(ToolKind.SupertonicVoice, "3-2026-05-11", "any",
        new Uri("https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/sherpa-onnx-supertonic-3-tts-int8-2026-05-11.tar.bz2"),
        "82fa96f91c4ef8abaae3a14a3f4153facf88bed821d1f7331cec2700f432c427", "sherpa-onnx-supertonic-3-tts-int8-2026-05-11", "tts.json", 128_774_318);
}
