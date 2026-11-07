using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>
/// Pinned voice models (sherpa-onnx tts-models release; hashes from the release). Supertonic 3: weights under OpenRAIL-M (use restrictions
/// passed on to users), code MIT. Kokoro 82M v1.0 (hexgrad, int8 by sherpa-onnx): Apache-2.0, not gated (checked on 2026-11-06); the archive
/// also holds espeak-ng data (GPL-3.0) that sherpa-onnx 2.x does not use: it is removed right after unpacking, so it is never kept. The Piper Turkish voice was removed in 0.1.1: sherpa-onnx 2.x dropped espeak-ng (GPL), so a Piper model
/// can no longer turn text into phonemes by itself, and no Turkish lexicon without espeak is available.
/// </summary>
public static class SpeechModels
{
    /// <summary>Supertonic 3 int8.</summary>
    public static readonly ToolArchive Supertonic = new(ToolKind.SupertonicVoice, "3-2026-05-11", "any",
        new Uri("https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/sherpa-onnx-supertonic-3-tts-int8-2026-05-11.tar.bz2"),
        "82fa96f91c4ef8abaae3a14a3f4153facf88bed821d1f7331cec2700f432c427", "sherpa-onnx-supertonic-3-tts-int8-2026-05-11", "tts.json", 128_774_318);

    /// <summary>Kokoro 82M v1.0 int8 (English voices are used; the dictionary frontend of sherpa-onnx 2.x).</summary>
    public static readonly ToolArchive Kokoro = new(ToolKind.KokoroVoice, "int8-multi-lang-v1_0", "any",
        new Uri("https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-int8-multi-lang-v1_0.tar.bz2"),
        "72c347230107ef8a31098ec4d468a605244beef5f1ed00db97efa13518800653", "kokoro-int8-multi-lang-v1_0", "model.int8.onnx", 132_353_410);

    /// <summary>
    /// Pocket TTS int8 (Kyutai, ONNX export by KevinAHM, packed by sherpa-onnx): weights CC-BY-4.0 (the archive's LICENSE; attribution in
    /// THIRD-PARTY-NOTICES), English only, speaks with a reference voice. The archive's README says "It is for non-commercial", which the
    /// licenses of the weights (CC-BY-4.0, kyutai/pocket-tts and KevinAHM/pocket-tts-onnx) do not; probably meant for its test voices,
    /// which are removed right after unpacking (their own licenses are not stated).
    /// </summary>
    public static readonly ToolArchive Pocket = new(ToolKind.PocketVoice, "int8-2026-01-26", "any",
        new Uri("https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/sherpa-onnx-pocket-tts-int8-2026-01-26.tar.bz2"),
        "2f3b88823cbbb9bf0b2477ec8ae7b3fec417b3a87b6bb5f256dba66f2ad967cb", "sherpa-onnx-pocket-tts-int8-2026-01-26", "lm_main.int8.onnx", 98_336_520);

    /// <summary>
    /// Folders of a voice archive that are never kept: espeak-ng data (GPL-3.0, unused by sherpa-onnx 2.x) and Pocket TTS's test voices
    /// (recordings of other people; the app speaks only with the user's own samples).
    /// </summary>
    public static readonly IReadOnlyList<string> Unused = ["espeak-ng-data", "test_wavs"];
}
