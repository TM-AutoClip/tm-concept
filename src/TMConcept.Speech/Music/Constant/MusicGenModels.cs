namespace TMConcept.Speech.Music;

/// <summary>
/// The MusicGen models (28 Nov 2026; Meta AudioCraft, run through Hugging Face transformers' MusicGen classes, not audiocraft itself, whose
/// PyTorch 2.1 pins cannot share anything with the app). Every checkpoint is Meta's own under <c>facebook/</c> on Hugging Face, not gated (no
/// sign-in), weights under CC-BY-NC-4.0 (non-commercial: the orange warning and the one-time question), code MIT. Instrumental music only, 32
/// kHz, at most 30 s a piece (1503 tokens; transformers has no continuation), a longer video loops it. Only the files transformers reads are
/// downloaded (the safetensors or, where Meta published none, the PyTorch files; configuration and tokenizer), each pinned by the repository's
/// commit, its SHA-256 (Git LFS ids from the Hugging Face API; small files measured on 28 Nov 2026) and its size.
/// <para>
/// Card classes (the catalog's <c>minVideoMemoryGb</c>; a melody or stereo model never below its plain twin, it has the same decoder): AudioCraft's
/// <c>docs/MUSICGEN.md</c> recommends a 16 GB card for the ~1.5B (medium) models, for its own longer, continued pieces ("smaller GPUs will be
/// able to generate short sequences"). The app makes one 30 s piece in 16 bits, so the classes are calculated: weights in 16 bits, plus the
/// key/value cache of 1503 tokens twice over (classifier-free guidance; layers × width × 2 × 2 bytes per token), plus about 1 GB of CUDA
/// context. Medium and melody (48 layers of 1536): 3.1 + 0.9 + 1 = about 5 GB, at the very limit of a 6 GB card: 8 GB. Large (48 layers of
/// 2048): 6.6 + 1.2 + 1 = about 8.8 GB: 12 GB. Small (24 layers of 1024): 0.6 + 0.3 + 1 = about 1.9 GB: 4 GB.
/// </para>
/// </summary>
public static class MusicGenModels
{
    /// <summary>The longest piece (seconds; "MusicGen cannot generate more than 30 seconds of audio (1503 tokens)", transformers' documentation).</summary>
    public const int MaxSeconds = 30;

    /// <summary>MusicGen Small (300M, mono): <c>facebook/musicgen-small</c> at 4c8334b, fp32 safetensors; about 2.37 GB.</summary>
    public static readonly LocalMusicVariant Small = new("musicgen-small", "musicgen-small", MaxSeconds, LocalMusicFiles.From("facebook/musicgen-small", "4c8334b02c6ec4e8664a91979669a501ec497792", "musicgen-small",
    [
        new("model.safetensors", Sha256: "1bdc99d43eb6c775967df24b65b0a9f847c0907e95664698d93b5a1c35f5090d", 2_364_427_288),
        new("config.json", Sha256: "36bba569a817511fa54ddd1c42b157c24c8f6a178b7b85484d7475e71ce30f6d", 7_866),
        new("generation_config.json", Sha256: "3126e10627aad71cce75772137b1043bae6a066751f0aaebf7a1ba440a5fc3c4", 224),
        new("preprocessor_config.json", Sha256: "3f820c9214aed413cde1d72fe2bed2f5ce5e5929d4ac2cc12bfddaf0ba0b8465", 275),
        new("special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer.json", Sha256: "5f0ed8ab5b8cfa9812bb73752f1d80c292e52bcf5a87a144dc9ab2d251056cbb", 2_422_095),
        new("tokenizer_config.json", Sha256: "88922cb67999604f60f3176db748b20975efc9ea7f72d5c80211be02d93c3b92", 2_366),
    ]));

    /// <summary>MusicGen Stereo Small (300M, stereo): <c>facebook/musicgen-stereo-small</c> at 3a110c0, fp16 safetensors (its fp32 copy is not downloaded); about 1.22 GB.</summary>
    public static readonly LocalMusicVariant StereoSmall = new("musicgen-stereo-small", "musicgen-stereo-small", MaxSeconds, LocalMusicFiles.From("facebook/musicgen-stereo-small", "3a110c0092820d470372c880c5ea06ce21a78ddd", "musicgen-stereo-small",
    [
        new("model.safetensors", Sha256: "0130758fdb8e4fda25f813b5f2301da70b197fef8ce7d92b6e8b97489ec745de", 1_215_815_916),
        new("config.json", Sha256: "3da474a14e5a002816462ff8e75b6c71392ce6acf36bdca0983f85d63fcd366d", 7_747),
        new("generation_config.json", Sha256: "0899bfea42953ac868181ba908bad55a3d7eedee96d686b6258269beb26b524c", 224),
        new("preprocessor_config.json", Sha256: "247807c6fc8025b6c2bce9c45b414d33fee91092e6ebce21ef18c4ef439c12b2", 275),
        new("special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer.json", Sha256: "1bbfe115157a6a057b4ed4f0a5fadc3515e92f69e8e8395d9bdbda9bbd258ced", 2_422_123),
        new("tokenizer_config.json", Sha256: "724108b918b10bab1fa57857cec9e45efaa04e734a85c7a9056371ab1d30081d", 20_788),
    ]));

    /// <summary>MusicGen Melody (1.5B, mono, text + melody): <c>facebook/musicgen-melody</c> at 68d653a, fp32 safetensors in two parts; about 6.23 GB.</summary>
    public static readonly LocalMusicVariant Melody = new("musicgen-melody", "musicgen-melody", MaxSeconds, LocalMusicFiles.From("facebook/musicgen-melody", "68d653a95788ec0d2b0abccab22c0b3a200c2d90", "musicgen-melody",
    [
        new("model-00001-of-00002.safetensors", Sha256: "3deb583fd977128b24f59c86d6706f01b35b4ab8553e8355cca6969a9b7440b4", 4_966_269_512),
        new("model-00002-of-00002.safetensors", Sha256: "01268b3a5f9819026a9ee6b76df35574e7dd521de3e6431c7fad16b44ce5a9c0", 1_263_380_400),
        new("model.safetensors.index.json", Sha256: "fbf97fdcda4fbcb6dc50404ce4b73e94e352aa091f91eff7174de00bc3a5598f", 68_461),
        new("config.json", Sha256: "16018e4e7906feea208af7ad0b30b175301ca8708b37f3743adf9a1fc565e8a6", 7_781),
        new("generation_config.json", Sha256: "0c496442cb17f12f111b0baaf340a3a015baf80083deb806fab90229eb8d6d14", 224),
        new("preprocessor_config.json", Sha256: "36784325e934e383bafadc6a3eb677a2c8bab251b2f27cfc212a9d89c170fb3a", 369),
        new("special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer.json", Sha256: "ca7dc3c343c9fb095db86dca7fe7945fff7608c25e170daf6c477bdca99b93ff", 2_422_346),
        new("tokenizer_config.json", Sha256: "cf78390648a76b34c0b3b24f2625521963360f6b386eb159129917243b6ecd41", 20_794),
    ]));

    /// <summary>MusicGen Stereo Melody (1.5B, stereo, text + melody): <c>facebook/musicgen-stereo-melody</c> at 022c3bd, fp32 safetensors in two parts; about 6.33 GB.</summary>
    public static readonly LocalMusicVariant StereoMelody = new("musicgen-stereo-melody", "musicgen-stereo-melody", MaxSeconds, LocalMusicFiles.From("facebook/musicgen-stereo-melody", "022c3bd20a7d77e7c014082f0391bcbcd3940a7a", "musicgen-stereo-melody",
    [
        new("model-00001-of-00002.safetensors", Sha256: "64bb17eb17182b17ba19dec21d794229c955d39cb5610a21da75ed2397da750d", 4_997_739_040),
        new("model-00002-of-00002.safetensors", Sha256: "95932e81bf5b2b02777a245a42dc65b9ab13362460ae77f7a1c3c0b50737e3c6", 1_332_599_632),
        new("model.safetensors.index.json", Sha256: "93e00571002070ca297a4835e41ef56d737e0f655137527a297bc20e057b484d", 69_085),
        new("config.json", Sha256: "9bf1104fe6070f70778e17e387672bd5d0e80fb081d1bb82e360003d01132a50", 7_781),
        new("generation_config.json", Sha256: "0c496442cb17f12f111b0baaf340a3a015baf80083deb806fab90229eb8d6d14", 224),
        new("preprocessor_config.json", Sha256: "36784325e934e383bafadc6a3eb677a2c8bab251b2f27cfc212a9d89c170fb3a", 369),
        new("special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer.json", Sha256: "ca7dc3c343c9fb095db86dca7fe7945fff7608c25e170daf6c477bdca99b93ff", 2_422_346),
        new("tokenizer_config.json", Sha256: "cf78390648a76b34c0b3b24f2625521963360f6b386eb159129917243b6ecd41", 20_794),
    ]));

    /// <summary>MusicGen Medium (1.5B, mono): <c>facebook/musicgen-medium</c> at d3bd7b0, an fp32 PyTorch file: Meta's revision has no safetensors (only an unmerged conversion bot pull request, not used); about 8.04 GB.</summary>
    public static readonly LocalMusicVariant Medium = new("musicgen-medium", "musicgen-medium", MaxSeconds, LocalMusicFiles.From("facebook/musicgen-medium", "d3bd7b00761b78ad7a8a05145ee31e7832e9916c", "musicgen-medium",
    [
        new("pytorch_model.bin", Sha256: "b51b0ec25096748bb9a86bf37e41b487024ab2b7077d26cc9e007525f47cd189", 8_042_351_585),
        new("config.json", Sha256: "5484e9b7f2789a77bcece6ed3b0d7c8a736e4ff4103e68724ae32e0e825eedfa", 7_866),
        new("generation_config.json", Sha256: "3126e10627aad71cce75772137b1043bae6a066751f0aaebf7a1ba440a5fc3c4", 224),
        new("preprocessor_config.json", Sha256: "3f820c9214aed413cde1d72fe2bed2f5ce5e5929d4ac2cc12bfddaf0ba0b8465", 275),
        new("special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer.json", Sha256: "5f0ed8ab5b8cfa9812bb73752f1d80c292e52bcf5a87a144dc9ab2d251056cbb", 2_422_095),
        new("tokenizer_config.json", Sha256: "88922cb67999604f60f3176db748b20975efc9ea7f72d5c80211be02d93c3b92", 2_366),
    ]));

    /// <summary>MusicGen Stereo Medium (1.5B, stereo): <c>facebook/musicgen-stereo-medium</c> at 2747e61, fp16 safetensors; about 4.07 GB.</summary>
    public static readonly LocalMusicVariant StereoMedium = new("musicgen-stereo-medium", "musicgen-stereo-medium", MaxSeconds, LocalMusicFiles.From("facebook/musicgen-stereo-medium", "2747e613fbaadc43b14257224326ef58c5c11b81", "musicgen-stereo-medium",
    [
        new("model.safetensors", Sha256: "6523842dae8a5b1003c00eb6a65d7fe753c2063b6ea55e320df8f16057f11771", 4_071_478_244),
        new("config.json", Sha256: "87eac65c05207a788342a5ef3f4bc849c1812d1e73b103857229e5f4ef22658a", 7_747),
        new("generation_config.json", Sha256: "0899bfea42953ac868181ba908bad55a3d7eedee96d686b6258269beb26b524c", 224),
        new("preprocessor_config.json", Sha256: "247807c6fc8025b6c2bce9c45b414d33fee91092e6ebce21ef18c4ef439c12b2", 275),
        new("special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer.json", Sha256: "1bbfe115157a6a057b4ed4f0a5fadc3515e92f69e8e8395d9bdbda9bbd258ced", 2_422_123),
        new("tokenizer_config.json", Sha256: "724108b918b10bab1fa57857cec9e45efaa04e734a85c7a9056371ab1d30081d", 20_788),
    ]));

    /// <summary>MusicGen Melody Large (3.3B, mono, text + melody): <c>facebook/musicgen-melody-large</c> at 6fdf8d3, fp32 safetensors in three parts; about 10.50 GB.</summary>
    public static readonly LocalMusicVariant MelodyLarge = new("musicgen-melody-large", "musicgen-melody-large", MaxSeconds, LocalMusicFiles.From("facebook/musicgen-melody-large", "6fdf8d3d815995108c9bdb5183414ff464b171ac", "musicgen-melody-large",
    [
        new("model-00001-of-00003.safetensors", Sha256: "a38e3ed0a1a1e9927278e20a2cb5e56bfce019b98f78295c99b9a747d9bda699", 4_986_994_376),
        new("model-00002-of-00003.safetensors", Sha256: "ff7f57ef40b093d1f69851ecde50f57eb89904aa2abd02912868447b73b212b8", 4_966_890_568),
        new("model-00003-of-00003.safetensors", Sha256: "b5aace972db765f0d5c9c7ba88119b1c1e7aae9fed43d226522f596af65c1ccf", 543_379_288),
        new("model.safetensors.index.json", Sha256: "87f872dcdd7c598e1d61b9a081328aefa51e83b22e5d139b9abe641c95568d67", 68_462),
        new("config.json", Sha256: "e17f0b50cb22b1e3f6db5117953918d3c2524716532c1886a12dec3997cb8a42", 7_781),
        new("generation_config.json", Sha256: "d7b18a12d2aaf81de615e5a333324107470ee92b6d93b84d09a345e68d9b8766", 224),
        new("preprocessor_config.json", Sha256: "36784325e934e383bafadc6a3eb677a2c8bab251b2f27cfc212a9d89c170fb3a", 369),
        new("special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer.json", Sha256: "a45ba2af379e1d559dbbdf176bb6a338945019534a20e15e07d22bbd0a7544b3", 2_422_191),
        new("tokenizer_config.json", Sha256: "cf78390648a76b34c0b3b24f2625521963360f6b386eb159129917243b6ecd41", 20_794),
    ]));

    /// <summary>MusicGen Stereo Melody Large (3.3B, stereo, text + melody): <c>facebook/musicgen-stereo-melody-large</c> at eea1486, fp32 safetensors in three parts; about 10.63 GB.</summary>
    public static readonly LocalMusicVariant StereoMelodyLarge = new("musicgen-stereo-melody-large", "musicgen-stereo-melody-large", MaxSeconds, LocalMusicFiles.From("facebook/musicgen-stereo-melody-large", "eea14861e6c29c47aea1055982a217468cd1634d", "musicgen-stereo-melody-large",
    [
        new("model-00001-of-00003.safetensors", Sha256: "de1f894560846ddcaa2c8a2259229e135bcab586ec5002145869777ce0a15a5a", 4_987_010_864),
        new("model-00002-of-00003.safetensors", Sha256: "1df2e518045a95c6ced5aaad33d34eea40346d7e158539595134dbff3b47e469", 4_966_907_208),
        new("model-00003-of-00003.safetensors", Sha256: "a0621f095b1fb03d926437ce35af9510f50438b7d15b237aae25c7d632f6c362", 677_597_536),
        new("model.safetensors.index.json", Sha256: "a2591c1b89a4db6ff44f71391a623ad4e6a9e104c339ca67646ac9c8d035001f", 69_086),
        new("config.json", Sha256: "db3766344726a0eef5ea13bea45cf0460704e7a6ca4a7f93991348a2430a6e29", 7_781),
        new("generation_config.json", Sha256: "0c496442cb17f12f111b0baaf340a3a015baf80083deb806fab90229eb8d6d14", 224),
        new("preprocessor_config.json", Sha256: "36784325e934e383bafadc6a3eb677a2c8bab251b2f27cfc212a9d89c170fb3a", 369),
        new("special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer.json", Sha256: "ca7dc3c343c9fb095db86dca7fe7945fff7608c25e170daf6c477bdca99b93ff", 2_422_346),
        new("tokenizer_config.json", Sha256: "cf78390648a76b34c0b3b24f2625521963360f6b386eb159129917243b6ecd41", 20_794),
    ]));

    /// <summary>MusicGen Large (3.3B, mono): <c>facebook/musicgen-large</c> at 15ccdc9, fp32 PyTorch files in two parts: Meta's revision has no safetensors; about 13.72 GB.</summary>
    public static readonly LocalMusicVariant Large = new("musicgen-large", "musicgen-large", MaxSeconds, LocalMusicFiles.From("facebook/musicgen-large", "15ccdc92099879e47b6da12c350cdb71d4eab3ca", "musicgen-large",
    [
        new("pytorch_model-00001-of-00002.bin", Sha256: "a7ed8a0d95eff206263d1ae62955a7b8a7dab11a6b55e953887aea6ed54f85bd", 9_987_827_539),
        new("pytorch_model-00002-of-00002.bin", Sha256: "f9923d2947297a9883404abfd4041a32cf486c763d36e00eb4f178823bf6bdac", 3_731_608_779),
        new("pytorch_model.bin.index.json", Sha256: "3119e53023fda91c46ad313372a258c71459ffb104fae0a09383b8a079c43572", 98_104),
        new("config.json", Sha256: "c3a17ac53995ad4911862c756dcd067869bc2b1bf05c54383b11d9434c06bb4d", 7_866),
        new("generation_config.json", Sha256: "3126e10627aad71cce75772137b1043bae6a066751f0aaebf7a1ba440a5fc3c4", 224),
        new("preprocessor_config.json", Sha256: "3f820c9214aed413cde1d72fe2bed2f5ce5e5929d4ac2cc12bfddaf0ba0b8465", 275),
        new("special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer.json", Sha256: "5f0ed8ab5b8cfa9812bb73752f1d80c292e52bcf5a87a144dc9ab2d251056cbb", 2_422_095),
        new("tokenizer_config.json", Sha256: "88922cb67999604f60f3176db748b20975efc9ea7f72d5c80211be02d93c3b92", 2_366),
    ]));

    /// <summary>MusicGen Stereo Large (3.3B, stereo): <c>facebook/musicgen-stereo-large</c> at bda8ec3, fp16 safetensors in two parts; about 6.93 GB.</summary>
    public static readonly LocalMusicVariant StereoLarge = new("musicgen-stereo-large", "musicgen-stereo-large", MaxSeconds, LocalMusicFiles.From("facebook/musicgen-stereo-large", "bda8ec330c9c17e09728589eaf678f58f7d2d932", "musicgen-stereo-large",
    [
        new("model-00001-of-00002.safetensors", Sha256: "2cdfc42b238184fe9b0d9690cc9681e5f4b06826c90ff533ba6e7031fb7cb25d", 4_993_882_540),
        new("model-00002-of-00002.safetensors", Sha256: "e011bbf8f4e0729f47f4bdaac10e3398fb59bb0833c02a5707ef618ccdeb1173", 1_932_912_112),
        new("model.safetensors.index.json", Sha256: "163ba18411dabc1226eed8ff97e0d836dfe1c6c2c91277a83531ae22c8b43608", 98_643),
        new("config.json", Sha256: "2cace810bcac4b99c771615bfea5e3fe7da8259c925a5e18155695e232d2cca4", 7_747),
        new("generation_config.json", Sha256: "0899bfea42953ac868181ba908bad55a3d7eedee96d686b6258269beb26b524c", 224),
        new("preprocessor_config.json", Sha256: "247807c6fc8025b6c2bce9c45b414d33fee91092e6ebce21ef18c4ef439c12b2", 275),
        new("special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer.json", Sha256: "1bbfe115157a6a057b4ed4f0a5fadc3515e92f69e8e8395d9bdbda9bbd258ced", 2_422_123),
        new("tokenizer_config.json", Sha256: "724108b918b10bab1fa57857cec9e45efaa04e734a85c7a9056371ab1d30081d", 20_788),
    ]));

    /// <summary>Every MusicGen model, in the catalog's order.</summary>
    public static readonly IReadOnlyList<LocalMusicVariant> All = [Small, StereoSmall, Melody, StereoMelody, Medium, StereoMedium, MelodyLarge, StereoMelodyLarge, Large, StereoLarge];
}
