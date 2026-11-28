using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The YuE models (28 Nov 2026; HKUST and M-A-P: lyrics to a whole song with vocals, or an instrumental piece without words). None of the
/// repositories is gated. Pins: Git LFS ids from the Hugging Face API, small files measured on 28 Nov 2026.
/// <para>
/// YuE2 (released 9 Sep 2026, <see cref="Yue2Models"/>): one 3.6B checkpoint (<c>m-a-p/YuE2-3B</c>) with its VAE (<c>m-a-p/YuE2-Vae</c>),
/// 48 kHz stereo, weights CC BY-NC 4.0 with a creator permission (individual creators may earn from what they make; companies need a licence:
/// non-commercial in the app, the orange warning and the one-time question). Two catalog entries share its files (downloaded once into
/// <c>yue2-3b/</c>): the official path (its model card: one song at a time on a 24 GB card, peaks of 11.2 to 14.1 GiB) and its low-memory mode
/// (<c>--low-vram</c>: the only official low-memory run is an 8 GB laptop card; measured peaks of 4.8 to 5.6 GiB on top of a fixed 2 GiB reserve,
/// so it does not fit 6 GB).
/// </para>
/// <para>
/// YuE 1 (<see cref="Yue1Models"/>, Apache-2.0: commercial use allowed), English chain of thought, two segments (about a minute): the
/// official 7B stage 1 (24 GB, the README's figure), its 4.25-bit exllamav2 quantisation (Doctor-Shotgun's; the only 6 GB run is the
/// YuE-exllamav2 README's RTX 3060 Laptop 6 GB, no peak published; its stage 1 needs about 6.1 to 6.5 GB, so a 6 GB card that also
/// draws the desktop spills into shared memory: class 8 GB since 0.1.59) and the 0.5B stage 1 (8 GB: its stage 2 is the original 16-bit 1B model
/// with batch size 4, which the same README's 6 GB table shows running out of memory; no official figure above that). Every YuE
/// 1 model needs the stage 2 model and the codec's weights (<c>m-a-p/xcodec_mini_infer</c>: its checkpoint, the two vocoder decoders and their
/// configurations), shared in <c>shared/</c>; the quantised one has its own stage 2.
/// </para>
/// </summary>
public static class YueModels
{
    /// <summary>The folder of YuE2's files (both entries).</summary>
    public const string Yue2Folder = "yue2-3b";

    /// <summary>The folder of the files the YuE 1 models share.</summary>
    public const string SharedFolder = "shared";

    /// <summary>The shared YuE 1 stage 2 model's folder (inside <see cref="SharedFolder"/>).</summary>
    public const string Stage2Folder = "s2-1b";

    /// <summary>The codec's folder (inside <see cref="SharedFolder"/>).</summary>
    public const string CodecFolder = "xcodec";

    /// <summary>The quantised model's stage 1 folder (inside its own folder).</summary>
    public const string QuantizedStage1Folder = "s1";

    /// <summary>The quantised model's stage 2 folder (inside its own folder).</summary>
    public const string QuantizedStage2Folder = "s2";

    /// <summary>YuE2's memory budget on its official path (GiB, its model card's 24 GB card).</summary>
    public const string FullBudget = "24";

    /// <summary>YuE2's memory budget in its low-memory mode (GiB, the official 8 GB runs).</summary>
    public const string LowMemoryBudget = "8";

    /// <summary>YuE2-3B (about 7.26 GB) and its VAE (about 0.53 GB), in <c>yue2-3b/model</c> and <c>yue2-3b/vae</c>.</summary>
    public static readonly IReadOnlyList<ToolArchive> Yue2Files =
    [
        .. LocalMusicFiles.From("m-a-p/YuE2-3B", "8f24312f187763b9854adeec87e437e2b03bbff1", Yue2Folder + "/model",
        [
            new("model.safetensors", Sha256: "1d55c42c1a9875c34f5d736e15078449992b044e807ce2a138e6cf289a1e59e9", 7_261_441_640),
            new("config.json", Sha256: "ad3477bbef890bf98ae196c1e4b44779494a6231c4ab66f32708eabadf265329", 959),
            new("generation_config.json", Sha256: "ccc842772f029049fadf9b288393bcdd9f79271b361c0f1875ec1218e4ba5170", 204),
            new("yue2_generation_config.json", Sha256: "203830cebde6e3644eb291925362990d198e66c3b6006cd11f1aaa21904bcc61", 466),
            new("weights_manifest.json", Sha256: "2296f82c29dcb11aeefde07e216403a32fc86c8f6da2ec15a70102abfd936a93", 179),
            new("qwen.tiktoken", Sha256: "b2b1b8dfb5cc5f024bafc373121c6aba3f66f9a5a0269e243470a1de16a33186", 2_561_218),
            new("modeling_yue2.py", Sha256: "62d2bd758c8fff1a0d64584a699343076fca9a7880db5d8e19263bc30ae79d7c", 32_983),
        ]),
        .. LocalMusicFiles.From("m-a-p/YuE2-Vae", "152733a19ad43aa67e367f9b5503ef8075bb5126", Yue2Folder + "/vae",
        [
            new("model.safetensors", Sha256: "807ce9d5149fa27c5ad3e6582058469852e908f6c5acc8c8aa338e7ab7751346", 530_512_720),
            new("config.json", Sha256: "f0191bb9694009956de44e0c361a6f1334760be4c8f848e599bde242a54a0970", 1_378),
            new("modeling_vae.py", Sha256: "b6578ff461e6be9fd079ca5ddf12893940f3b3338ff3435fceae89c466486f5a", 25_265),
            new("weights_manifest.json", Sha256: "017d64a4d288217a43fcac3866a0e1d55f796fa891d4dfb6de0bd170ab750026", 178),
        ]),
    ];

    /// <summary>YuE2 3B on its official path (24 GB card; up to 6 minutes, its 9,000-token cap).</summary>
    public static readonly LocalMusicVariant Yue2 = new("yue2-3b", "yue2-3b-full", 360, Yue2Files, Yue2Folder,
        [new LocalMusicOption("--budget", FullBudget)]);

    /// <summary>YuE2 3B in its low-memory mode (8 GB card; up to 4 minutes, the length of the official 8 GB runs).</summary>
    public static readonly LocalMusicVariant Yue2LowMemory = new("yue2-3b-lowvram", "yue2-3b-lowvram", 240, Yue2Files, Yue2Folder,
        [new LocalMusicOption("--budget", LowMemoryBudget), new LocalMusicOption("--low-vram")]);

    /// <summary>The YuE 1 stage 2 model (16-bit, about 3.93 GB), shared by the 7B and 0.5B models in <c>shared/s2-1b</c>.</summary>
    public static readonly IReadOnlyList<ToolArchive> Stage2 = LocalMusicFiles.From("m-a-p/YuE-s2-1B-general", "9dfa90b7013f6b5e7eb5eb2991620dca33058a0e", SharedFolder + "/" + Stage2Folder,
    [
        new("model.safetensors", Sha256: "87839fa9693a15d84c6bc96df2ac270063d4d87598b0fd0555996fe934fdf42f", 3_925_119_920),
        new("config.json", Sha256: "f2413fea99d2d9645a2c1901a73125043011d455780fbebfdc22a1e6d43e3967", 683),
        new("generation_config.json", Sha256: "b4fd5e73f5528179169ef43cde9a9044cd7ddce39a0012d418b18fa346e2caab", 111),
        new("tokenizer.model", Sha256: "ee5c7cbf32da93989f14d9ba635e3e1d1ab2cc88a92908a5ed0f149375f6ee49", 1_761_962),
    ]);

    /// <summary>The codec's weights every YuE 1 model needs (about 1.51 GB), in <c>shared/xcodec</c>.</summary>
    public static readonly IReadOnlyList<ToolArchive> Codec = LocalMusicFiles.From("m-a-p/xcodec_mini_infer", "fe781a67815ab47b4a3a5fce1e8d0a692da7e4e5", SharedFolder + "/" + CodecFolder,
    [
        new("final_ckpt/ckpt_00360000.pth", Sha256: "c8c379ea2d3cbde1c8ba1b9717975220e79ba3f556bb161766fd5e4585dcd59c", 1_360_444_883),
        new("final_ckpt/config.yaml", Sha256: "35a3d0d553065844cc92dd4f972bf09af785ecdefc5921b117858a9c807c8680", 2_863),
        new("decoders/decoder_131000.pth", Sha256: "b99f0be84eeef3a32f29cd55beb89727fd0b2fd0df3dbad3023508f4c7185c37", 72_611_958),
        new("decoders/decoder_151000.pth", Sha256: "8af97a29d3483f9d4a3755992837501bd7d6caa1a69382ed16e64039e0ea0998", 72_610_550),
        new("decoders/config.yaml", Sha256: "5a0f1dd895d767ae5dd19323ba0f86fa15177d814e01f79edf9309ce164d5b0b", 269),
    ]);

    /// <summary>YuE 1 7B, English chain of thought (16-bit, 24 GB card): <c>m-a-p/YuE-s1-7B-anneal-en-cot</c> at 454c20e, about 12.45 GB plus the shared files.</summary>
    public static readonly LocalMusicVariant Stage1Large = new("yue-v1-s1-7b-cot", "yue-v1-s1-7b-cot", 60, [.. LocalMusicFiles.From("m-a-p/YuE-s1-7B-anneal-en-cot", "454c20e1748888800f8e4b3da45125f55482d967", "yue-v1-s1-7b-cot",
    [
        new("model-00001-of-00003.safetensors", Sha256: "93c6f48ec95c0e36e681fb1c200754b9e39b9d7603ebdee88580429b526a86b2", 4_915_933_984),
        new("model-00002-of-00003.safetensors", Sha256: "d9a7c7adf0142010ea7fb2d6d60b2698b86f36847d00d0afa4170c3a9fb66a9c", 4_934_842_808),
        new("model-00003-of-00003.safetensors", Sha256: "c4c9f1d21524ad189e63230a62a62997c52205f9ce3099948c7fc3d27385d0dc", 2_598_483_736),
        new("model.safetensors.index.json", Sha256: "b86397fcbcbb9c5f0164f16e02157f4aa2ab29df07cac347a6f955d5a5d7f38b", 23_950),
        new("config.json", Sha256: "5fa0931414299cd622f6cba8f66c9e397306a622cd9b7703e771b051f9819017", 684),
        new("generation_config.json", Sha256: "b4fd5e73f5528179169ef43cde9a9044cd7ddce39a0012d418b18fa346e2caab", 111),
        new("tokenizer.model", Sha256: "ee5c7cbf32da93989f14d9ba635e3e1d1ab2cc88a92908a5ed0f149375f6ee49", 1_761_962),
    ]), .. Stage2, .. Codec], SharedFolder,
    [
        new LocalMusicOption("--stage1", "yue-v1-s1-7b-cot", InFamilyFolder: true),
        new LocalMusicOption("--stage2", SharedFolder + "/" + Stage2Folder, InFamilyFolder: true),
    ]);

    /// <summary>
    /// YuE 1 7B through exllamav2 (stage 1 at 4.25 bits, stage 2 at 8 bits; Doctor-Shotgun's quantisations, about 5.76 GB plus the codec):
    /// <c>Doctor-Shotgun/YuE-s1-7B-anneal-en-cot-exl2</c> (branch 4.25bpw-h6 at b7037dc) in <c>yue-v1-exl2/s1</c> and
    /// <c>Doctor-Shotgun/YuE-s2-1B-general-exl2</c> (branch 8.0bpw-h8 at 674b44b) in <c>yue-v1-exl2/s2</c>.
    /// </summary>
    public static readonly LocalMusicVariant Quantized = new("yue-v1-exl2", "yue-v1-exl2", 60,
    [
        .. LocalMusicFiles.From("Doctor-Shotgun/YuE-s1-7B-anneal-en-cot-exl2", "b7037dcd1d1ec09a6df600424e8b6acdbca4d96b", "yue-v1-exl2/" + QuantizedStage1Folder,
        [
            new("output.safetensors", Sha256: "c70515ed09a77e2bb25a19bd0d1eab8c37054e073f2849f1348c53cf1882ba3d", 3_900_817_438),
            new("config.json", Sha256: "f308eac466072b08ce889302b7e8283497ed65ea1f05ea038493af84a238ceba", 1_043),
            new("generation_config.json", Sha256: "b4fd5e73f5528179169ef43cde9a9044cd7ddce39a0012d418b18fa346e2caab", 111),
            new("tokenizer.model", Sha256: "ee5c7cbf32da93989f14d9ba635e3e1d1ab2cc88a92908a5ed0f149375f6ee49", 1_761_962),
        ]),
        .. LocalMusicFiles.From("Doctor-Shotgun/YuE-s2-1B-general-exl2", "674b44b254f01516a256bebef45deb600bdf33f2", "yue-v1-exl2/" + QuantizedStage2Folder,
        [
            new("output.safetensors", Sha256: "29a541f70ba360bd7aa2beaedecaaa6ee59475206c4cc2a09d9054efcee78e9c", 1_851_357_198),
            new("config.json", Sha256: "a83514fe82247bd38b0ad6a4334b4996a41bc8ddaaa021aec9fa683cacd7e489", 1_041),
            new("generation_config.json", Sha256: "b4fd5e73f5528179169ef43cde9a9044cd7ddce39a0012d418b18fa346e2caab", 111),
            new("tokenizer.model", Sha256: "ee5c7cbf32da93989f14d9ba635e3e1d1ab2cc88a92908a5ed0f149375f6ee49", 1_761_962),
        ]),
        .. Codec,
    ], SharedFolder,
    [
        new LocalMusicOption("--stage1", "yue-v1-exl2/" + QuantizedStage1Folder, InFamilyFolder: true),
        new LocalMusicOption("--stage2", "yue-v1-exl2/" + QuantizedStage2Folder, InFamilyFolder: true),
        new LocalMusicOption("--exl2"),
    ]);

    /// <summary>YuE 1 0.5B stage 1 (English; 16-bit): <c>m-a-p/YuE-s1-0.5B</c> at 65b4514, about 0.96 GB plus the shared files.</summary>
    public static readonly LocalMusicVariant Stage1Small = new("yue-v1-s1-0.5b", "yue-v1-s1-0.5b", 60, [.. LocalMusicFiles.From("m-a-p/YuE-s1-0.5B", "65b4514ee5d821275cf4204f307425ed94fb936e", "yue-v1-s1-0.5b",
    [
        new("model.safetensors", Sha256: "5a1656531af9afee1520a056515f7f9192831bed0c0fa7514e7f642ebf9ddfb8", 960_096_632),
        new("config.json", Sha256: "5e130d6804e6739f459ab151b2cf315fbea96bb846a394615367b09d43e7adb6", 684),
        new("generation_config.json", Sha256: "b4fd5e73f5528179169ef43cde9a9044cd7ddce39a0012d418b18fa346e2caab", 111),
        new("tokenizer.model", Sha256: "ee5c7cbf32da93989f14d9ba635e3e1d1ab2cc88a92908a5ed0f149375f6ee49", 1_761_962),
    ]), .. Stage2, .. Codec], SharedFolder,
    [
        new LocalMusicOption("--stage1", "yue-v1-s1-0.5b", InFamilyFolder: true),
        new LocalMusicOption("--stage2", SharedFolder + "/" + Stage2Folder, InFamilyFolder: true),
    ]);

    /// <summary>The YuE2 models, in the catalog's order.</summary>
    public static readonly IReadOnlyList<LocalMusicVariant> Yue2Models = [Yue2, Yue2LowMemory];

    /// <summary>The YuE 1 models, in the catalog's order.</summary>
    public static readonly IReadOnlyList<LocalMusicVariant> Yue1Models = [Stage1Large, Quantized, Stage1Small];
}
