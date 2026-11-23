using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;

namespace TMConcept.Speech.Python;

/// <summary>
/// The Python voices (16 Nov 2026): what the course project's engines need (repository, files, packages, sample rate, voice cloning), here
/// pinned to a Hugging Face commit with every file's SHA-256 (the LFS values of the Hub API; the small files hashed after downloading them on
/// 16 Nov 2026). Only the files the voice reads are downloaded (no samples, no training files, no second checkpoint). Licences from the model
/// cards: XTTS-v2 Coqui Public Model License (non-commercial), F5-TTS Turkish CC BY-NC 4.0, MMS-TTS CC BY-NC 4.0, Trendyol-TTS MIT,
/// VoxCPM2 Apache-2.0, Ahenk CC BY 4.0 (attribution). Not added: Kokoro and Chatterbox on PyTorch (their ONNX builds are already in the app),
/// Piper Turkish (its voice is CC BY-NC-SA and it needs espeak-ng), OmniVoice and Fish S2 Pro (non-commercial weights and a server of their
/// own), EMA Lightning (no pinned way to fetch its weights without its package downloading them).
/// </summary>
public static class PythonVoices
{
    private const string XttsCommit = "6c2b0d75eae4b7047358e3b6bd9325f857d43f77";
    private const string F5Commit = "219efea2f2da1c363b00360141af8344dd488214";
    private const string VocosCommit = "0feb3fdd929bcd6649e0e7c5a688cf7dd012ef21";
    private const string VoxCpmCommit = "7936080b41163874f38610f46f438e2c0757f9fd";
    private const string TrendyolCommit = "66a80184b286390800ee7c1a95228cc839cd59ef";
    private const string AhenkCommit = "6bcef95c9e8401058253518d08028369958522cc";
    private const string MmsCommit = "7e364479c307f06733ca865b0a5269e0209cf82d";
    private const string YazgiCommit = "87c89ad84556fb3f1c171f7f88a37ddc0f806b04";

    private static readonly IReadOnlyList<string> Turkish = [ContentLanguages.Turkish];

    /// <summary>
    /// XTTS-v2 (Coqui; the maintained coqui-tts package): 17 languages, ten of them content languages here; built-in speakers or the user's
    /// own sample. About 3 GB of graphics memory; on the processor several times slower than the speech.
    /// </summary>
    public static readonly PythonVoice XttsV2 = new(SpeechEngines.XttsV2, "XTTS-v2", PythonEnvironments.Xtts.Key, "xtts",
    [
        PythonModelFiles.Pinned("coqui/XTTS-v2", XttsCommit, "config.json", "ef262b1454dd2a77e1461b0b2cd53e19b8a7624cc131b837d36df67356bc75e8", 4_368),
        PythonModelFiles.Pinned("coqui/XTTS-v2", XttsCommit, "vocab.json", "928260878a59da8a72a2a5b7687fea29d5106137669d90945430fe17e415304a", 361_219),
        PythonModelFiles.Pinned("coqui/XTTS-v2", XttsCommit, "mel_stats.pth", "1f69422a8a8f344c4fca2f0c6b8d41d2151d6615b7321e48e6bb15ae949b119c", 1_067),
        PythonModelFiles.Pinned("coqui/XTTS-v2", XttsCommit, "speakers_xtts.pth", "f0f6137c19a4eab0cbbe4c99b5babacf68b1746e50da90807708c10e645b943b", 7_754_818),
        PythonModelFiles.Pinned("coqui/XTTS-v2", XttsCommit, "model.pth", "c7ea20001c6a0a841c77e252d8409f6a74fb423e79b3206a0771ba5989776187", 1_867_929_118),
    ],
    [
        ContentLanguages.Turkish, ContentLanguages.English, ContentLanguages.German, ContentLanguages.Spanish, ContentLanguages.French,
        ContentLanguages.Italian, ContentLanguages.Portuguese, ContentLanguages.Dutch, ContentLanguages.Polish, ContentLanguages.Russian,
    ],
    [
        new("Ana Florence", "Ana Florence"), new("Claribel Dervla", "Claribel Dervla"), new("Daisy Studious", "Daisy Studious"),
        new("Damien Black", "Damien Black"), new("Viktor Eka", "Viktor Eka"), new("Andrew Chipper", "Andrew Chipper"),
    ],
    CopiesVoice: true, OnCard: true, NeedMegabytes: 3_072, new Dictionary<string, string> { ["speaker"] = "Ana Florence" });

    /// <summary>
    /// F5-TTS with the Turkish fine-tune of marduk-ra (1 000 000 steps) and the Vocos vocoder (MIT): Turkish only, in the voice of the
    /// reference clip that comes with the app.
    /// </summary>
    public static readonly PythonVoice F5Turkish = new(SpeechEngines.F5Turkish, "F5-TTS Türkçe", PythonEnvironments.F5.Key, "f5",
    [
        PythonModelFiles.Pinned("marduk-ra/F5-TTS-Turkish", F5Commit, "vocab.txt", "99cc6b6089e4fa44b22b95a6666a303680a14bbff0616ab5e76dff2abc37fcb1", 11_285),
        PythonModelFiles.Pinned("marduk-ra/F5-TTS-Turkish", F5Commit, "f5_tts_turkish_1000000.safetensors",
            "7c155e48a34a02b90684605c51ebe1beb857342707132334214ab13c8427a21c", 1_348_454_193),
        PythonModelFiles.Pinned("charactr/vocos-mel-24khz", VocosCommit, "config.yaml", "da9033922f969a47f0c160010226919e59f27761fd5066f3828d46de6650b0fc", 461,
            "vocos/config.yaml"),
        PythonModelFiles.Pinned("charactr/vocos-mel-24khz", VocosCommit, "pytorch_model.bin", "97ec976ad1fd67a33ab2682d29c0ac7df85234fae875aefcc5fb215681a91b2a",
            54_365_991, "vocos/pytorch_model.bin"),
    ],
    Turkish, [new("reference", "Türkçe anlatıcı")], CopiesVoice: false, OnCard: true, NeedMegabytes: 3_072, new Dictionary<string, string>());

    /// <summary>VoxCPM2 (OpenBMB, 2B, 48 kHz): every content language here; two described narrators or the user's own sample.</summary>
    public static readonly PythonVoice VoxCpm2 = new(SpeechEngines.VoxCpm2, "VoxCPM2", PythonEnvironments.VoxCpm.Key, "voxcpm", PythonModelFiles.VoxCpm("openbmb/VoxCPM2",
        VoxCpmCommit, "f7f964cfa9da23653baec6e6f7750719977ad944ed9f95fe52fe3a620506891d", 4_580_080_592),
    [
        ContentLanguages.Turkish, ContentLanguages.English, ContentLanguages.German, ContentLanguages.Spanish, ContentLanguages.French,
        ContentLanguages.Italian, ContentLanguages.Portuguese, ContentLanguages.Dutch, ContentLanguages.Polish, ContentLanguages.Russian,
        ContentLanguages.Swedish, ContentLanguages.Indonesian,
    ],
    [new("narrator-female", "Narrator (female, described)"), new("narrator-male", "Narrator (male, described)")],
    CopiesVoice: true, OnCard: true, NeedMegabytes: 8_192, new Dictionary<string, string> { ["kind"] = "voxcpm2" });

    /// <summary>Trendyol-TTS: VoxCPM2 with Trendyol's Turkish LoRA merged in (MIT); Turkish only, its own voice.</summary>
    public static readonly PythonVoice Trendyol = new(SpeechEngines.Trendyol, "Trendyol-TTS", PythonEnvironments.VoxCpm.Key, "voxcpm", PythonModelFiles.VoxCpm("Trendyol/Trendyol-TTS",
        TrendyolCommit, "70073e6d60adbceceea60ba3eb027fb5c57688cf40553f6e221a5546e7c87f9a", 4_580_080_752),
        Turkish, [new("default", "Trendyol-TTS")], CopiesVoice: false, OnCard: true, NeedMegabytes: 8_192, new Dictionary<string, string> { ["kind"] = "trendyol" });

    /// <summary>
    /// Ahenk (Özgür Güler): a Turkish LoRA for VoxCPM2 with one designed voice (f022) and six emotions; built on the VoxCPM2 model folder
    /// (<see cref="PythonVoice.BaseVoice"/>), so VoxCPM2 is downloaded with it.
    /// </summary>
    public static readonly PythonVoice Ahenk = new(SpeechEngines.Ahenk, "Ahenk", PythonEnvironments.VoxCpm.Key, "voxcpm",
    [
        PythonModelFiles.Pinned("ozgurguler/ahenk-tts-tr", AhenkCommit, "lora_config.json", "2e354446a4799237a8b0c971778713dea1ec85d298e4b2ece322c2970581d091", 514),
        PythonModelFiles.Pinned("ozgurguler/ahenk-tts-tr", AhenkCommit, "instruction_pick.py", "69e5f2de0327e45a1049d161e29c927e9636e6f09beabea7628f349c7410b707", 9_767),
        PythonModelFiles.Pinned("ozgurguler/ahenk-tts-tr", AhenkCommit, "instructions_tr.json", "5d1e18c62fe8ad75863abbca37878aef5730c6787619c4e599d7c454b98882b4", 7_870),
        PythonModelFiles.Pinned("ozgurguler/ahenk-tts-tr", AhenkCommit, "pause_clean.py", "a8952114562fff1db399f96d8b14eff2709a5cc79bd4ba8f975d4786af0b68f3", 15_042),
        PythonModelFiles.Pinned("ozgurguler/ahenk-tts-tr", AhenkCommit, "onset.py", "3d37228997f6a3936087514c54ab9778851a8a12b83c4b36bd90b6d2d9cc3118", 24_350),
        PythonModelFiles.Pinned("ozgurguler/ahenk-tts-tr", AhenkCommit, "lora_weights.safetensors", "0d6362c273b0dac67787ec966a5bbd719b3850cbfb1df567044df153d9886019",
            144_749_240),
    ],
    Turkish,
    [
        new("neutral", "Düz"), new("calm", "Sakin"), new("cheerful", "Neşeli"), new("empathetic", "Empatik"), new("sad", "Hüzünlü"), new("angry", "Kızgın"),
        new("surprised", "Şaşkın"),
    ],
    CopiesVoice: false, OnCard: true, NeedMegabytes: 8_192, new Dictionary<string, string> { ["kind"] = "ahenk" }, BaseVoice: SpeechEngines.VoxCpm2);

    /// <summary>MMS-TTS Turkish (Meta, VITS, 145 MB): Turkish only, one voice, always on the processor (fast enough there).</summary>
    public static readonly PythonVoice MmsTurkish = new(SpeechEngines.MmsTurkish, "MMS-TTS Türkçe", PythonEnvironments.Mms.Key, "mms",
    [
        PythonModelFiles.Pinned("facebook/mms-tts-tur", MmsCommit, "config.json", "d0378ee0c78342b0e6ccaa43a2c7d93949ca93cd6e74ea5ede0937533c8d9b2a", 1_641),
        PythonModelFiles.Pinned("facebook/mms-tts-tur", MmsCommit, "special_tokens_map.json", "4f1fb0d57b85d32ae624d33babc8b7505b714cf4dc5ec70d2bd2c8d0854ffcc1", 47),
        PythonModelFiles.Pinned("facebook/mms-tts-tur", MmsCommit, "tokenizer_config.json", "a07edf50f10b44ece48b92649fea4a59aa2b45466bc91ded8278960d8e36c530", 287),
        PythonModelFiles.Pinned("facebook/mms-tts-tur", MmsCommit, "vocab.json", "4aa3eb05485270578ba8445a0ba9b61e3b748aef733b35bbf927d3e46a47412c", 476),
        PythonModelFiles.Pinned("facebook/mms-tts-tur", MmsCommit, "model.safetensors", "927a00f1d0a3b3ae41ddd8f0d196be8343ceb2a13599cd6e84ef7e882ca58525", 145_231_352),
    ],
    Turkish, [new("default", "MMS")], CopiesVoice: false, OnCard: false, NeedMegabytes: 0, new Dictionary<string, string>());

    /// <summary>Every Python voice.</summary>
    /// <summary>
    /// yazgı-tts-1 (0.1.35): a small Turkish voice on the processor; its package is the pinned wheel in the model folder, its weights the pinned
    /// .pt files loaded as plain tensors (torch.load weights_only=True, inside the package). Eight built-in voices; not a default.
    /// </summary>
    public static readonly PythonVoice YazgiTts = new(SpeechEngines.YazgiTts, "yazgı-tts-1", PythonEnvironments.Yazgi.Key, "yazgi",
    [
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "config.json", "084fb8024d78abc7011ef7e71e42ca5dbbf35f70e814e6c7c4204724ec4bbd7a", 960),
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "model.pt", "ba8acbd3831062a5ed8a7bd0822fc21f6c97bbb9c7992df7afd20958a2fd3041", 225_063_723),
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "codec.pt", "34bb58a62d31341a800ec57569036c044bb8882693a4990fa6675c1d0787c70b", 387_579_887),
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "dist/yazgi_tts-0.1.0-py3-none-any.whl", "69aa6de7812dee43cf1f1a51fe62823b4a8fb270befd37920e3e16c13fb92c23", 32_077,
            "yazgi_tts-0.1.0-py3-none-any.whl"),
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "voices/female-01.npz", "c5bb96636f771ff121f5609c32369b8b38fefa4933c56c658521b9bb3aa6c70d", 3_926, "voices/female-01.npz"),
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "voices/female-02.npz", "ebf6aa7355a23d69f52a987e0b6decc264ec8dc7f6f9ea74fb95ca7142b82331", 3_382, "voices/female-02.npz"),
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "voices/female-03.npz", "f92303a09a4bca707ac5a2b48aa297d8846195d9f163e684d2f2db6d41b64370", 3_926, "voices/female-03.npz"),
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "voices/female-04.npz", "c62fb8bd84795741c9cacc36b383472821dec6b3edb86f6df30cfceaf6036fc4", 3_766, "voices/female-04.npz"),
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "voices/male-01.npz", "96e402930b217eb699efff754ac4382e431994c74e75c3a970e9d80e64a14354", 3_254, "voices/male-01.npz"),
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "voices/male-02.npz", "50d4c31331c8390d7400cc763dda66c03245ca051602ab72ea02645c37244135", 3_382, "voices/male-02.npz"),
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "voices/male-03.npz", "82256e9062d8915a59302c3b307014c635cc3c9ae5404c6b8623cc3abb36f19d", 3_510, "voices/male-03.npz"),
        PythonModelFiles.Pinned("erkamk/yazgi-tts-1", YazgiCommit, "voices/male-04.npz", "e939c9d178ae17eae841b2bbc84fd20ef5188ed6b7e388f670a265d481a04242", 3_638, "voices/male-04.npz"),
    ],
    Turkish,
    [
        new("male-01", "Erkek 1"), new("male-02", "Erkek 2"), new("male-03", "Erkek 3"), new("male-04", "Erkek 4"),
        new("female-01", "Kadın 1"), new("female-02", "Kadın 2"), new("female-03", "Kadın 3"), new("female-04", "Kadın 4"),
    ],
    CopiesVoice: false, OnCard: false, NeedMegabytes: 0, new Dictionary<string, string>());

    public static readonly IReadOnlyList<PythonVoice> All = [XttsV2, F5Turkish, VoxCpm2, Trendyol, Ahenk, MmsTurkish, YazgiTts];
}
