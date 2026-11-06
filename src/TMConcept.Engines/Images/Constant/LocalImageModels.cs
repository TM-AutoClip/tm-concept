namespace TMConcept.Engines.Images;

/// <summary>
/// The two local image models of the prototype's Görsel Üretim page, with its measured settings (RTX 3060 Laptop, 6 GB, 1280×720: FLUX.2
/// klein ~20 s, Z-Image Turbo Q8 ~71 s). Every file is Apache-2.0 and not gated (checked on Hugging Face on 2026-11-06): FLUX.2 klein 4B
/// (Black Forest Labs; the 9B model is non-commercial and is not used), its GGUF by leejet, the FLUX.2 VAE (Comfy-Org), Qwen3-4B (Qwen,
/// GGUF by unsloth); Z-Image Turbo (Tongyi-MAI; GGUF by leejet), its VAE (Comfy-Org), Qwen3-4B-Instruct-2507 (GGUF by unsloth). Sizes and
/// SHA-256 are the Hugging Face LFS values (also the prototype's pins); every file is fetched at the repository commit that was current on
/// 2026-11-06 (checked: the same SHA-256 there), so a file uploaded again later cannot break the install. The weights are downloaded at run time from huggingface.co after a
/// confirmation; the app does not ship them.
/// </summary>
public static class LocalImageModels
{
    /// <summary>
    /// The flags the prototype measured for a 6 GB card: flash attention, tiled VAE, a 4 GB graph budget. Measured on 6 GB only, so the
    /// catalog asks for at least 6 GB for both models (a 4 GB card would leave no room next to the 4 GB budget).
    /// </summary>
    public static readonly IReadOnlyList<string> MemoryFlags = ["--diffusion-fa", "--vae-tiling", "--max-vram", "4"];

    /// <summary>FLUX.2 klein 4B (Q4_0): the fastest.</summary>
    public static readonly LocalImageModel Flux2Klein = new("flux2-klein-4b", "FLUX.2 klein 4B",
        HuggingFaceFile.Pinned("leejet/FLUX.2-klein-4B-GGUF", "3b1f5a9dc3abb32238b053aeb3d823c30afdacbd", "flux-2-klein-4b-Q4_0.gguf", "d1023499ef3f2f82ff7c50e6778495195c1b6cc34835741778868428111f9ff4", 2_460_378_560),
        HuggingFaceFile.Pinned("unsloth/Qwen3-4B-GGUF", "22c9fc8a8c7700b76a1789366280a6a5a1ad1120", "Qwen3-4B-Q4_K_M.gguf", "f6f851777709861056efcdad3af01da38b31223a3ba26e61a4f8bf3a2195813a", 2_497_281_312),
        HuggingFaceFile.Pinned("Comfy-Org/Flux-2-Klein-4B", "5b3abca5c0dc1ecd4eaf3340868d09063fc1c965", "split_files/vae/flux2-vae.safetensors", "868fe7b343cc8f3a19dbcfcafbc3d5f888802be3f89bd81b65b3621a066ce8f3",
            336_211_292),
        4, MemoryFlags);

    /// <summary>Z-Image Turbo (Q8_0): better pictures, slower.</summary>
    public static readonly LocalImageModel ZImageTurbo = new("z-image-turbo", "Z-Image Turbo",
        HuggingFaceFile.Pinned("leejet/Z-Image-Turbo-GGUF", "c61c0e422dc8b541b7548cf33a4ef8302b0f8085", "z_image_turbo-Q8_0.gguf", "df1c5baa86d1398c979495a6072dbcee79444fdb884a2445582ba0769c44e9a1", 6_577_440_704),
        HuggingFaceFile.Pinned("unsloth/Qwen3-4B-Instruct-2507-GGUF", "a06e946bb6b655725eafa393f4a9745d460374c9",
            "Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
            "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597", 2_497_281_120),
        HuggingFaceFile.Pinned("Comfy-Org/Z-Image-Turbo", "6fc90a3b1b653e935a0d175e260736de25b84df5", "split_files/vae/ae.safetensors", "afc8e28272cd15db3919bacdb6918ce9c1ed22e96cb12c4d5ed0fba823529e38", 335_304_388),
        8, MemoryFlags);

    /// <summary>Both models.</summary>
    public static readonly IReadOnlyList<LocalImageModel> All = [Flux2Klein, ZImageTurbo];
}
