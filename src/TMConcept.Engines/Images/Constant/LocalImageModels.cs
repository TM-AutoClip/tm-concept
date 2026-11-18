using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Engines.Images;

/// <summary>
/// The local image models (0.1.17: the course's seven and higher-quality variants for the larger memory classes). Every file is fetched from
/// huggingface.co at a pinned repository commit with its SHA-256 and size (the Hugging Face API's LFS values, every one checked again at its
/// commit on 2026-11-16), so a file uploaded again later cannot break the install; nothing is gated, the app ships no weights, and a model is
/// downloaded only when the user picks it for a step and confirms the download.
/// <para>Memory classes (<see cref="VramTier"/>; a card sees its own class and every lower one): the class a model runs in comfortably with
/// the memory options of <see cref="ImageMemoryPlanner"/> (weights in main memory where the card is too small). Measured only on a 6 GB RTX
/// 3060 Laptop at 1280×720 (FLUX.2 klein 4B Q4 about 20 s, Z-Image Turbo Q8 about 71 s); the others are estimates from the file sizes.</para>
/// <para>Licenses: Z-Image Turbo, FLUX.2 klein 4B, Qwen-Image and every text encoder (Qwen3, Qwen3-VL, Qwen2.5-VL) Apache-2.0;
/// Qwen-Image 2.1 (its Turbo and its VAE too) is under the Qwen Research License: non-commercial; the FLUX.2 VAE
/// is the Apache-2.0 copy of Comfy-Org/Flux-2-Klein-4B (not the one of FLUX.2 dev, which is non-commercial); FLUX.2 klein 9B is
/// non-commercial; FLUX.3 Image Klein (FLUX Kommunity License) and Stable Diffusion 3.5 Medium (Stability AI Community License) allow
/// commercial use only under conditions (a revenue limit); SDXL Lightning is OpenRAIL++ (use restrictions), its fp16 VAE MIT. Anything but
/// free commercial use is shown in the picker and asked before the first download.</para>
/// </summary>
public static class LocalImageModels
{
    /// <summary>Default negative prompt of the models guided with a scale above 1.</summary>
    public const string DefaultNegativePrompt = "blurry, low quality, lowres, distorted, deformed, watermark, jpeg artifacts";

    /// <summary>The oldest stable-diffusion.cpp build that knows FLUX.3 (master-968-8cd731e, "add FLUX.3 Image Klein").</summary>
    public const int Flux3Build = 968;

    /// <summary>
    /// The oldest stable-diffusion.cpp build for Qwen-Image 2.1 (master-959-d637cfb, "add Qwen Image 2.1 Turbo sigma preset"; the model itself
    /// came in master-883, its schedule and VAE fixes in master-913/914). Both pinned sd-cli builds (Windows 1106, Linux 1043) are newer.
    /// </summary>
    public const int QwenImage21Build = 959;

    /// <summary>The fixed 8-step schedule of Qwen-Image 2.1 Turbo by its sd-cli preset name (without it sd-cli uses the base model's schedule).</summary>
    public static readonly IReadOnlyList<string> QwenImage21TurboSigmas = ["--sigmas", "qwen-image-2.1-turbo"];

    private const string FluxNonCommercialUrl = "https://huggingface.co/black-forest-labs/FLUX.2-klein-9B";
    private const string FluxKommunityUrl = "https://huggingface.co/black-forest-labs/flux-3-image-klein";
    private const string StabilityCommunityUrl = "https://huggingface.co/stabilityai/stable-diffusion-3.5-medium";
    private const string SdxlLightningUrl = "https://huggingface.co/ByteDance/SDXL-Lightning";
    private const string QwenResearchUrl = "https://huggingface.co/Qwen/Qwen-Image-2.1";

    private const string ZImageGguf = "c61c0e422dc8b541b7548cf33a4ef8302b0f8085";
    private const string ZImageComfy = "6fc90a3b1b653e935a0d175e260736de25b84df5";
    private const string Qwen3Instruct = "a06e946bb6b655725eafa393f4a9745d460374c9";
    private const string Flux2Klein4Gguf = "3b1f5a9dc3abb32238b053aeb3d823c30afdacbd";
    private const string Flux2Klein9Gguf = "cc588497a95ffc2937ebbd6b9b3916a11ada6e5b";
    private const string Qwen3Base4 = "22c9fc8a8c7700b76a1789366280a6a5a1ad1120";
    private const string Qwen3Base8 = "a6adef130ffb23ddaf1a62fec9dced968c9bc482";
    private const string Flux3Gguf = "57dfa91df2284f4985a4107c399d05d12e0f9b63";
    private const string Qwen3Vl = "1cd86afb9a95c410a6038ab3b40d8b578c892266";
    private const string Sd35Gguf = "58b78c305a43ddfcffe1ab54d7022995f61667ac";
    private const string SdxlLightningGguf = "4fe21f71b298e088be341bb6545f6a2df950b5ff";
    private const string QwenImageGguf = "257f261fa92593bed760aa6fa3f7921a49fea00f";
    private const string Qwen25Vl = "cfa2baa09946b211c107e6e104948987a64dd2c1";
    private const string QwenImage21Comfy = "aacf31a6d48e707f352ecba714cee0680da3479b";
    private const string QwenImage21Gguf = "0bca76da6edd8a18211e9717454615c1bbaaf6ed";
    private const string QwenImage21TurboGguf = "cbcb9ff83b17e1eeef693479a8d34cea355a0ee4";
    private const string Qwen3Vl8 = "f982a07559d4a2f6c8744d840bf6fccab30eea96";

    /// <summary>The Z-Image VAE (Comfy-Org, Apache-2.0).</summary>
    public static readonly ImageModelFile ZImageVae = new(ImageFileRole.Vae, HuggingFaceFile.Pinned("Comfy-Org/Z-Image-Turbo", ZImageComfy,
        "split_files/vae/ae.safetensors", "afc8e28272cd15db3919bacdb6918ce9c1ed22e96cb12c4d5ed0fba823529e38", 335_304_388));

    /// <summary>The FLUX.2 VAE (the Apache-2.0 copy of Comfy-Org/Flux-2-Klein-4B); FLUX.3 uses it too.</summary>
    public static readonly ImageModelFile Flux2Vae = new(ImageFileRole.Vae, HuggingFaceFile.Pinned("Comfy-Org/Flux-2-Klein-4B", "5b3abca5c0dc1ecd4eaf3340868d09063fc1c965",
        "split_files/vae/flux2-vae.safetensors", "868fe7b343cc8f3a19dbcfcafbc3d5f888802be3f89bd81b65b3621a066ce8f3", 336_211_292));

    /// <summary>The Qwen-Image VAE (Comfy-Org, Apache-2.0).</summary>
    public static readonly ImageModelFile QwenImageVae = new(ImageFileRole.Vae, HuggingFaceFile.Pinned("Comfy-Org/Qwen-Image_ComfyUI", "1f12b17be14c89b026c51a91d67c32f84bb047bc",
        "split_files/vae/qwen_image_vae.safetensors", "a70580f0213e67967ee9c95f05bb400e8fb08307e017a924bf3441223e023d1f", 253_806_246));

    /// <summary>The Qwen-Image 2.1 VAE (Comfy-Org's copy; Qwen Research License like the model; not interchangeable with the Qwen-Image VAE).</summary>
    public static readonly ImageModelFile QwenImage21Vae = new(ImageFileRole.Vae, HuggingFaceFile.Pinned("Comfy-Org/Qwen-Image-2.1", QwenImage21Comfy,
        "vae/qwen_image_2.1_vae_bf16.safetensors", "bb21f7473051e1ac368515dd3f2e15cd44d7a11748ee8823e1ddca3e4876b7c9", 675_509_688)); // SHA-256 (HF LFS)

    /// <summary>The SDXL VAE that works in fp16 (madebyollin, MIT).</summary>
    public static readonly ImageModelFile SdxlVae = new(ImageFileRole.Vae, HuggingFaceFile.Pinned("madebyollin/sdxl-vae-fp16-fix", "207b116dae70ace3637169f1ddd2434b91b3a8cd",
        "sdxl_vae.safetensors", "235745af8d86bf4a4c1b5b4f529868b37019a10f7c0b2e79ad0abca3a22bc6e1", 334_641_162));

    private static readonly ImageModelFile Qwen3InstructQ4 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("unsloth/Qwen3-4B-Instruct-2507-GGUF", Qwen3Instruct,
        "Qwen3-4B-Instruct-2507-Q4_K_M.gguf", "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597", 2_497_281_120));

    private static readonly ImageModelFile Qwen3InstructQ8 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("unsloth/Qwen3-4B-Instruct-2507-GGUF", Qwen3Instruct,
        "Qwen3-4B-Instruct-2507-Q8_0.gguf", "391c1e410fd9f4cf2de2b510273b56a84c19ce18f4fa3bfb3774031dac4ef068", 4_280_405_600));

    private static readonly ImageModelFile Qwen3Q4 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("unsloth/Qwen3-4B-GGUF", Qwen3Base4,
        "Qwen3-4B-Q4_K_M.gguf", "f6f851777709861056efcdad3af01da38b31223a3ba26e61a4f8bf3a2195813a", 2_497_281_312));

    private static readonly ImageModelFile Qwen3Q8 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("unsloth/Qwen3-4B-GGUF", Qwen3Base4,
        "Qwen3-4B-Q8_0.gguf", "eed555233267a33c7e8ee31682762cc7751b3f6d224039086e0e846f05fffa5d", 4_280_405_792));

    private static readonly ImageModelFile Qwen3Of8Q4 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("unsloth/Qwen3-8B-GGUF", Qwen3Base8,
        "Qwen3-8B-Q4_K_M.gguf", "120307ba529eb2439d6c430d94104dabd578497bc7bfe7e322b5d9933b449bd4", 5_027_784_512));

    private static readonly ImageModelFile Qwen3Of8Q8 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("unsloth/Qwen3-8B-GGUF", Qwen3Base8,
        "Qwen3-8B-Q8_0.gguf", "0cfbf745760f07a76ddeb358dd025a27f2e11d1ca9c9a4169a373d52990fe86e", 8_709_519_168));

    private static readonly ImageModelFile Qwen3VlQ4 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("Qwen/Qwen3-VL-4B-Instruct-GGUF", Qwen3Vl,
        "Qwen3VL-4B-Instruct-Q4_K_M.gguf", "66358cb18bb6b3b1b6675aa412c7a88ef01d228f481184d13668e5201c730a0a", 2_497_281_664));

    private static readonly ImageModelFile Qwen3VlQ8 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("Qwen/Qwen3-VL-4B-Instruct-GGUF", Qwen3Vl,
        "Qwen3VL-4B-Instruct-Q8_0.gguf", "054721f478bc5fa6beffb7f38eae575d45298f88cbb8d2f83ef675a727863eb1", 4_280_406_144));

    private static readonly ImageModelFile Qwen25VlQ4 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("mradermacher/Qwen2.5-VL-7B-Instruct-GGUF", Qwen25Vl,
        "Qwen2.5-VL-7B-Instruct.Q4_K_M.gguf", "0f00a930ba3108b6861ddadf74d8ebbd82e257c63eba728e62c3e8970f5eed94", 4_683_072_512));

    private static readonly ImageModelFile Qwen25VlQ8 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("mradermacher/Qwen2.5-VL-7B-Instruct-GGUF", Qwen25Vl,
        "Qwen2.5-VL-7B-Instruct.Q8_0.gguf", "577bfb3e41f93f00414e594c56a38bbe91207b3b636ab7faf343e34a20aa73ca", 8_098_524_160));

    private static readonly ImageModelFile Qwen3Vl8Q4 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("Qwen/Qwen3-VL-8B-Instruct-GGUF", Qwen3Vl8,
        "Qwen3VL-8B-Instruct-Q4_K_M.gguf", "67d1659bfe71b89d50b45a4ad1a9e5b997e5bb16ce5da66a6a6167abd569e9e2", 5_027_784_800)); // SHA-256 (HF LFS)

    private static readonly ImageModelFile Qwen3Vl8Q8 = new(ImageFileRole.Llm, HuggingFaceFile.Pinned("Qwen/Qwen3-VL-8B-Instruct-GGUF", Qwen3Vl8,
        "Qwen3VL-8B-Instruct-Q8_0.gguf", "0d264b3941185d00a74f75c4245521dae088ff1efc90ab8d1754e83f5844adb0", 8_709_519_456)); // SHA-256 (HF LFS)

    private static readonly ImageModelFile Sd35ClipL = new(ImageFileRole.ClipL, HuggingFaceFile.Pinned("second-state/stable-diffusion-3.5-medium-GGUF", Sd35Gguf,
        "clip_l-Q8_0.gguf", "482dfa677edb6499a689f1936f287351f3c104d5da9094d8c797da527a98323b", 130_864_000));

    private static readonly ImageModelFile Sd35ClipG = new(ImageFileRole.ClipG, HuggingFaceFile.Pinned("second-state/stable-diffusion-3.5-medium-GGUF", Sd35Gguf,
        "clip_g-Q8_0.gguf", "0678985268cc46e8b61ae3671dc1909566b8d6d20a2a2e4b10c7402b59c7dd5a", 738_543_360));

    /// <summary>Z-Image Turbo Q3_K (the course's model; sd.cpp's docs: runs in 4 GB or less): the smallest Z-Image.</summary>
    public static readonly LocalImageModel ZImageTurboSmall = new("z-image-turbo-q3", "Z-Image Turbo (small)",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("leejet/Z-Image-Turbo-GGUF", ZImageGguf, "z_image_turbo-Q3_K.gguf",
            "4b44bdaa7814f20d7cf144e3939bd93aa32f50660204dd0c2aea5c5376232980", 3_143_559_104)),
        Qwen3InstructQ4, ZImageVae,
    ])
    {
        Family = "z-image", Steps = 8, Tier = VramTier.Gb4,
    };

    /// <summary>FLUX.2 klein 4B (Q4_0): the fastest (measured about 20 s on a 6 GB card).</summary>
    public static readonly LocalImageModel Flux2Klein = new("flux2-klein-4b", "FLUX.2 klein 4B",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("leejet/FLUX.2-klein-4B-GGUF", Flux2Klein4Gguf, "flux-2-klein-4b-Q4_0.gguf",
            "d1023499ef3f2f82ff7c50e6778495195c1b6cc34835741778868428111f9ff4", 2_460_378_560)),
        Qwen3Q4, Flux2Vae,
    ])
    {
        Family = "flux2", Steps = 4, Tier = VramTier.Gb4,
    };

    /// <summary>SDXL Lightning 4-step (Q4_0) with the fp16 VAE: small and quick; made for about one megapixel.</summary>
    public static readonly LocalImageModel SdxlLightning = new("sdxl-lightning", "SDXL Lightning",
    [
        new(ImageFileRole.Full, HuggingFaceFile.Pinned("mzwing/SDXL-Lightning-GGUF", SdxlLightningGguf, "sdxl_lightning_4step.q4_0.gguf",
            "33cc25b6f5dd121ecf3ad28e35eb38cb21427b7794c53527ceafe2dc3b109f4f", 2_584_858_432)),
        SdxlVae,
    ])
    {
        Family = "sdxl", Steps = 4, Scheduler = "sgm_uniform", MaxPixels = 1_048_576, Tier = VramTier.Gb4,
        License = "openrail-plusplus", LicenseUse = LicenseUse.Limited, LicenseUrl = SdxlLightningUrl,
    };

    /// <summary>Z-Image Turbo (Q8_0 with the Q4 text encoder): better pictures, slower (measured about 71 s on a 6 GB card).</summary>
    public static readonly LocalImageModel ZImageTurbo = new("z-image-turbo", "Z-Image Turbo",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("leejet/Z-Image-Turbo-GGUF", ZImageGguf, "z_image_turbo-Q8_0.gguf",
            "df1c5baa86d1398c979495a6072dbcee79444fdb884a2445582ba0769c44e9a1", 6_577_440_704)),
        Qwen3InstructQ4, ZImageVae,
    ])
    {
        Family = "z-image", Steps = 8, Tier = VramTier.Gb6,
    };

    /// <summary>FLUX.3 Image Klein (Q4_K) with the Qwen3-VL 4B text encoder: 16 steps; needs sd-cli master-968 or newer.</summary>
    public static readonly LocalImageModel Flux3Klein = new("flux3-klein", "FLUX.3 Image Klein",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("leejet/flux-3-image-klein-GGUF", Flux3Gguf, "flux3-image-klein-Q4_K.gguf",
            "19ddbfbed61da4bb16ebf169aaf48e9e5db43aba557be1ee537e9f00f586092e", 3_514_023_104)),
        Qwen3VlQ4, Flux2Vae,
    ])
    {
        Family = "flux3", Steps = 16, Tier = VramTier.Gb6, MinSdCppBuild = Flux3Build,
        License = "flux-kommunity", LicenseUse = LicenseUse.Limited, LicenseUrl = FluxKommunityUrl,
    };

    /// <summary>Stable Diffusion 3.5 Medium (Q4_0, T5-XXL Q4_0): guided (cfg 4.5, skip-layer guidance 2.5), 28 steps; made for about one megapixel.</summary>
    public static readonly LocalImageModel Sd35Medium = new("sd35-medium", "Stable Diffusion 3.5 Medium",
    [
        new(ImageFileRole.Full, HuggingFaceFile.Pinned("second-state/stable-diffusion-3.5-medium-GGUF", Sd35Gguf, "sd3.5_medium-Q4_0.gguf",
            "3bb8c5e9ab0a841117089ed4ed81d885bb85161df2a766b812f829bc55b31adf", 2_079_836_768)),
        Sd35ClipL, Sd35ClipG,
        new(ImageFileRole.T5xxl, HuggingFaceFile.Pinned("second-state/stable-diffusion-3.5-medium-GGUF", Sd35Gguf, "t5xxl-Q4_0.gguf",
            "987ba47c158b890c274f78fd35324419f50941e846a49789f0977e9fe9d97ab7", 2_752_841_312)),
    ])
    {
        Family = "sd3", Steps = 28, CfgScale = 4.5, ExtraArguments = ["--slg-scale", "2.5"], MaxPixels = 1_048_576, NegativePrompt = DefaultNegativePrompt,
        Tier = VramTier.Gb6, License = "stability-community", LicenseUse = LicenseUse.Limited, LicenseUrl = StabilityCommunityUrl,
    };

    /// <summary>
    /// Qwen-Image 2.1 Turbo (AtomicChat's importance-matrix Q4_K, 4.2 GB, built and checked with stable-diffusion.cpp; Qwen3-VL 8B Q4_K_M):
    /// 8 steps on its own sigma schedule, no guidance; strong at text in pictures. Qwen Research License: NON-COMMERCIAL; asked before the download.
    /// </summary>
    public static readonly LocalImageModel QwenImage21Turbo = new("qwen-image-2.1-turbo", "Qwen-Image 2.1 Turbo",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("AtomicChat/Qwen-Image-2.1-Turbo-GGUF", QwenImage21TurboGguf, "Qwen-Image-2.1-Turbo-AD-Q4_K.gguf",
            "4bb73c53cbe284bbd6d69b9fdc59539c531f0389dd2aa567663d777f8130bc59", 4_201_694_944)), // SHA-256 (HF LFS)
        Qwen3Vl8Q4, QwenImage21Vae,
    ])
    {
        Family = "qwen-image-2.1", Steps = 8, ExtraArguments = QwenImage21TurboSigmas, Tier = VramTier.Gb6, MinSdCppBuild = QwenImage21Build,
        License = "qwen-research", LicenseUse = LicenseUse.NonCommercial, LicenseUrl = QwenResearchUrl,
    };

    /// <summary>SDXL Lightning at Q8_0: sharper than Q4.</summary>
    public static readonly LocalImageModel SdxlLightningQ8 = new("sdxl-lightning-q8", "SDXL Lightning (Q8)",
    [
        new(ImageFileRole.Full, HuggingFaceFile.Pinned("mzwing/SDXL-Lightning-GGUF", SdxlLightningGguf, "sdxl_lightning_4step.q8_0.gguf",
            "3a8b2ea93d0b0c5e72c82ed518aea715203173e2afbf8b7e47335d7f8799ef78", 4_098_988_672)),
        SdxlVae,
    ])
    {
        Family = "sdxl", Steps = 4, Scheduler = "sgm_uniform", MaxPixels = 1_048_576, Tier = VramTier.Gb6,
        License = "openrail-plusplus", LicenseUse = LicenseUse.Limited, LicenseUrl = SdxlLightningUrl,
    };

    /// <summary>FLUX.2 klein 9B (Q4_0, Qwen3-8B): NON-COMMERCIAL license; asked before the download.</summary>
    public static readonly LocalImageModel Flux2Klein9 = new("flux2-klein-9b", "FLUX.2 klein 9B",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("leejet/FLUX.2-klein-9B-GGUF", Flux2Klein9Gguf, "flux-2-klein-9b-Q4_0.gguf",
            "a7e77afa96871d16679ff7b949bd25f20c8179f219c4b662cac91e81ed99b944", 5_616_208_032)),
        Qwen3Of8Q4, Flux2Vae,
    ])
    {
        Family = "flux2", Steps = 4, Tier = VramTier.Gb8,
        License = "flux-non-commercial", LicenseUse = LicenseUse.NonCommercial, LicenseUrl = FluxNonCommercialUrl,
    };

    /// <summary>Z-Image Turbo Q8_0 with the Q8_0 text encoder (research: the right weights for 12–16 GB cards instead of BF16).</summary>
    public static readonly LocalImageModel ZImageTurboQ8 = new("z-image-turbo-q8", "Z-Image Turbo (Q8)",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("leejet/Z-Image-Turbo-GGUF", ZImageGguf, "z_image_turbo-Q8_0.gguf",
            "df1c5baa86d1398c979495a6072dbcee79444fdb884a2445582ba0769c44e9a1", 6_577_440_704)),
        Qwen3InstructQ8, ZImageVae,
    ])
    {
        Family = "z-image", Steps = 8, Tier = VramTier.Gb12,
    };

    /// <summary>FLUX.2 klein 4B at Q8_0 with Qwen3-4B Q8_0: the best quality per GB for 12 GB cards (Apache-2.0).</summary>
    public static readonly LocalImageModel Flux2KleinQ8 = new("flux2-klein-4b-q8", "FLUX.2 klein 4B (Q8)",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("leejet/FLUX.2-klein-4B-GGUF", Flux2Klein4Gguf, "flux-2-klein-4b-Q8_0.gguf",
            "0bba6951258ec8f92d51114a8fa13e66828297bfff58a738f52729b3ef66fa28", 4_300_629_440)),
        Qwen3Q8, Flux2Vae,
    ])
    {
        Family = "flux2", Steps = 4, Tier = VramTier.Gb12,
    };

    /// <summary>Qwen-Image 2.1 Turbo at Q8_0 with Qwen3-VL 8B Q8_0: NON-COMMERCIAL license; asked before the download.</summary>
    public static readonly LocalImageModel QwenImage21TurboQ8 = new("qwen-image-2.1-turbo-q8", "Qwen-Image 2.1 Turbo (Q8)",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("AtomicChat/Qwen-Image-2.1-Turbo-GGUF", QwenImage21TurboGguf, "Qwen-Image-2.1-Turbo-Q8_0.gguf",
            "8d74ae0be534df394ea2d29f04b77f879318c9e0df20a3e208e6bedbf89b4159", 7_591_577_312)), // SHA-256 (HF LFS)
        Qwen3Vl8Q8, QwenImage21Vae,
    ])
    {
        Family = "qwen-image-2.1", Steps = 8, ExtraArguments = QwenImage21TurboSigmas, Tier = VramTier.Gb12, MinSdCppBuild = QwenImage21Build,
        License = "qwen-research", LicenseUse = LicenseUse.NonCommercial, LicenseUrl = QwenResearchUrl,
    };

    /// <summary>Stable Diffusion 3.5 Medium at Q8_0 with T5-XXL Q8_0.</summary>
    public static readonly LocalImageModel Sd35MediumQ8 = new("sd35-medium-q8", "Stable Diffusion 3.5 Medium (Q8)",
    [
        new(ImageFileRole.Full, HuggingFaceFile.Pinned("second-state/stable-diffusion-3.5-medium-GGUF", Sd35Gguf, "sd3.5_medium-Q8_0.gguf",
            "d59aa31f4ec70fa0189f8baaeacbb91048a80bf289b5cd958795f62988e4aa48", 3_191_079_008)),
        Sd35ClipL, Sd35ClipG,
        new(ImageFileRole.T5xxl, HuggingFaceFile.Pinned("second-state/stable-diffusion-3.5-medium-GGUF", Sd35Gguf, "t5xxl-Q8_0.gguf",
            "fc07757bf7ad40eaf612acc7ed0c0a7ab71189979e0b8b4d14601017baca22de", 5_199_794_784)),
    ])
    {
        Family = "sd3", Steps = 28, CfgScale = 4.5, ExtraArguments = ["--slg-scale", "2.5"], MaxPixels = 1_048_576, NegativePrompt = DefaultNegativePrompt,
        Tier = VramTier.Gb12, License = "stability-community", LicenseUse = LicenseUse.Limited, LicenseUrl = StabilityCommunityUrl,
    };

    /// <summary>Qwen-Image (Q4_0, Qwen2.5-VL 7B Q4_K_M): strong at text in pictures; guided (cfg 2.5, flow shift 3), 20 steps.</summary>
    public static readonly LocalImageModel QwenImage = new("qwen-image", "Qwen-Image",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("QuantStack/Qwen-Image-GGUF", QwenImageGguf, "Qwen_Image-Q4_0.gguf",
            "ab0f20d548f3bfb197ec0f0f61c15acc2f0f865495fee2941c658fef4391cd61", 11_852_773_920)),
        Qwen25VlQ4, QwenImageVae,
    ])
    {
        Family = "qwen-image", Steps = 20, CfgScale = 2.5, FlowShift = 3, NegativePrompt = DefaultNegativePrompt, Tier = VramTier.Gb16,
    };

    /// <summary>
    /// Qwen-Image 2.1, the undistilled model (leejet's Q8_0, Qwen3-VL 8B Q8_0): guided (cfg 6 as in sd.cpp's docs, 40 steps as on Qwen's card),
    /// about ten times the work of the Turbo one for the finest pictures. NON-COMMERCIAL license; asked before the download.
    /// </summary>
    public static readonly LocalImageModel QwenImage21 = new("qwen-image-2.1", "Qwen-Image 2.1",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("leejet/Qwen-Image-2.1-GGUF", QwenImage21Gguf, "qwen_image_2.1-Q8_0.gguf",
            "f8b244b00937f0e444a40dbf7866460871b89b30142594973b6012d1b471dc0a", 7_687_155_744)), // SHA-256 (HF LFS)
        Qwen3Vl8Q8, QwenImage21Vae,
    ])
    {
        Family = "qwen-image-2.1", Steps = 40, CfgScale = 6, NegativePrompt = DefaultNegativePrompt, Tier = VramTier.Gb16, MinSdCppBuild = QwenImage21Build,
        License = "qwen-research", LicenseUse = LicenseUse.NonCommercial, LicenseUrl = QwenResearchUrl,
    };

    /// <summary>FLUX.3 Image Klein at Q8_0 with Qwen3-VL 4B Q8_0.</summary>
    public static readonly LocalImageModel Flux3KleinQ8 = new("flux3-klein-q8", "FLUX.3 Image Klein (Q8)",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("leejet/flux-3-image-klein-GGUF", Flux3Gguf, "flux3-image-klein-Q8_0.gguf",
            "8b71ffd749bcacbd50cc0acca4f2d31a044c94412743e1e780db99bf13f89752", 6_151_716_032)),
        Qwen3VlQ8, Flux2Vae,
    ])
    {
        Family = "flux3", Steps = 16, Tier = VramTier.Gb16, MinSdCppBuild = Flux3Build,
        License = "flux-kommunity", LicenseUse = LicenseUse.Limited, LicenseUrl = FluxKommunityUrl,
    };

    /// <summary>
    /// Z-Image Turbo at full precision (BF16 diffusion model and BF16 Qwen3-4B, ~20.7 GB of files; the files sd.cpp's own docs use): the
    /// best Z-Image pictures, for 24 GB cards (on 16 GB cards the Q8 one is the right choice; 0.1.16's untiled VAE failed there).
    /// </summary>
    public static readonly LocalImageModel ZImageTurboFull = new("z-image-turbo-full", "Z-Image Turbo (full quality)",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("Comfy-Org/Z-Image-Turbo", ZImageComfy, "split_files/diffusion_models/z_image_turbo_bf16.safetensors",
            "2407613050b809ffdff18a4ac99af83ea6b95443ecebdf80e064a79c825574a6", 12_309_866_400)),
        new(ImageFileRole.Llm, HuggingFaceFile.Pinned("Comfy-Org/Z-Image-Turbo", ZImageComfy, "split_files/text_encoders/qwen_3_4b.safetensors",
            "6c671498573ac2f7a5501502ccce8d2b08ea6ca2f661c458e708f36b36edfc5a", 8_044_982_048)),
        ZImageVae,
    ])
    {
        Family = "z-image", Steps = 8, Tier = VramTier.Gb24,
    };

    /// <summary>FLUX.2 klein 9B at Q8_0 with Qwen3-8B Q8_0: NON-COMMERCIAL license; asked before the download.</summary>
    public static readonly LocalImageModel Flux2Klein9Q8 = new("flux2-klein-9b-q8", "FLUX.2 klein 9B (Q8)",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("leejet/FLUX.2-klein-9B-GGUF", Flux2Klein9Gguf, "flux-2-klein-9b-Q8_0.gguf",
            "67ca777c7aa2d6a0d63d4a7564f823c63af88e9688643df727e1867789062982", 9_978_284_192)),
        Qwen3Of8Q8, Flux2Vae,
    ])
    {
        Family = "flux2", Steps = 4, Tier = VramTier.Gb24,
        License = "flux-non-commercial", LicenseUse = LicenseUse.NonCommercial, LicenseUrl = FluxNonCommercialUrl,
    };

    /// <summary>Qwen-Image at Q6_K (16.8 GB diffusion model).</summary>
    public static readonly LocalImageModel QwenImageQ6 = new("qwen-image-q6", "Qwen-Image (Q6)",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("QuantStack/Qwen-Image-GGUF", QwenImageGguf, "Qwen_Image-Q6_K.gguf",
            "0c60643ee869161ee7acd530f7faf1cb2361f131e1ebae27b95fdc481b6df2b2", 16_824_990_240)),
        Qwen25VlQ4, QwenImageVae,
    ])
    {
        Family = "qwen-image", Steps = 20, CfgScale = 2.5, FlowShift = 3, NegativePrompt = DefaultNegativePrompt, Tier = VramTier.Gb24,
    };

    /// <summary>Qwen-Image at Q8_0 with Qwen2.5-VL 7B Q8_0 (21.8 GB diffusion model): for 32 GB cards.</summary>
    public static readonly LocalImageModel QwenImageQ8 = new("qwen-image-q8", "Qwen-Image (Q8)",
    [
        new(ImageFileRole.Diffusion, HuggingFaceFile.Pinned("QuantStack/Qwen-Image-GGUF", QwenImageGguf, "Qwen_Image-Q8_0.gguf",
            "43142b32778dc0568c27395abc6f8291ac53f1de0b98eed919712cf161f248de", 21_761_817_120)),
        Qwen25VlQ8, QwenImageVae,
    ])
    {
        Family = "qwen-image", Steps = 20, CfgScale = 2.5, FlowShift = 3, NegativePrompt = DefaultNegativePrompt, Tier = VramTier.Gb32,
    };

    /// <summary>Every local image model, by memory class.</summary>
    public static readonly IReadOnlyList<LocalImageModel> All =
    [
        ZImageTurboSmall, Flux2Klein, SdxlLightning,
        ZImageTurbo, Flux3Klein, Sd35Medium, SdxlLightningQ8, QwenImage21Turbo,
        Flux2Klein9,
        ZImageTurboQ8, Flux2KleinQ8, Sd35MediumQ8, QwenImage21TurboQ8,
        QwenImage, Flux3KleinQ8, QwenImage21,
        ZImageTurboFull, Flux2Klein9Q8, QwenImageQ6,
        QwenImageQ8,
    ];
}
