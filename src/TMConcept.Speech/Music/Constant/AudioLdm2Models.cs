namespace TMConcept.Speech.Music;

/// <summary>
/// The AudioLDM 2 models (28 Nov 2026; CVSSP Surrey, latent diffusion run through diffusers' <c>AudioLDM2Pipeline</c>). 16 kHz mono, trained on
/// 10.24 s clips: the app asks at most 30 s (Large: 20 s, the tightest on a 6 GB card), a longer video loops it; no singing. The three
/// official repositories under <c>cvssp/</c> are not gated, the weights CC-BY-NC-SA-4.0 (non-commercial and share-alike: the orange warning and
/// the one-time question). Each model is its whole diffusers folder (26 files: configuration, tokenizers, the CLAP and Flan-T5 text encoders,
/// GPT-2, UNet, VAE, vocoder), safetensors only; most files are the same in all three, but diffusers reads one folder, so each model keeps
/// its own copy. Pins: Git LFS ids from the Hugging Face API, small files measured on 28 Nov 2026.
/// </summary>
public static class AudioLdm2Models
{
    /// <summary>AudioLDM 2 Music (350M UNet, 1.1B in all): <c>cvssp/audioldm2-music</c> at 0019cb0, the diffusers layout (safetensors only); about 4.48 GB.</summary>
    public static readonly LocalMusicVariant Music = new("audioldm2-music", "audioldm2-music", 30, LocalMusicFiles.From("cvssp/audioldm2-music", "0019cb0bd4d314accff72aee1c3abd6f22929ba5", "audioldm2-music",
    [
        new("feature_extractor/preprocessor_config.json", Sha256: "9739f58296aa6f9ac18008fd0150fb2649bc554985fbde86d0a4041c882ac753", 541),
        new("language_model/config.json", Sha256: "dfd666349774232748f24a9fcc71c1cec577af0b9a1a18846cedbc37bbbf87bb", 902),
        new("language_model/model.safetensors", Sha256: "8c57787555e1feb378e69e676a9d4f384363150dee6eeef390c47c1a5a99525d", 497_772_432),
        new("projection_model/config.json", Sha256: "78d94ba74ecacd622e206e6d751810c7bebd46a70632c318ae159056f100058a", 173),
        new("projection_model/diffusion_pytorch_model.safetensors", Sha256: "2d58536f307aec6d114f8017c0c0e46762a35bce76f418f8ff7cee0c68ebc8f8", 4_737_688),
        new("scheduler/scheduler_config.json", Sha256: "b6d79c593aa336438b0826c5c00273e3c773283f6df0f365d1e908a291ec5367", 507),
        new("text_encoder/config.json", Sha256: "8680493b8b6622bf44565aaa69c80a9da28be20577bba2ef14095c845fdb1d17", 801),
        new("text_encoder/model.safetensors", Sha256: "a4a47b4a637dd58e9edb7b64a06acf37328b7cc3eafb0b8a85df895cc9e45d09", 776_327_432),
        new("text_encoder_2/config.json", Sha256: "45c375b0b91e81ff41960cac9e9c7bef294ca637fee1e3c40a8c12001190a0fd", 766),
        new("text_encoder_2/model.safetensors", Sha256: "c1d0c8f1c739db9343c12ea4b0e3f2c97a833b3c072c251e91d97b7326fefb4e", 1_364_951_064),
        new("tokenizer/merges.txt", Sha256: "1ce1664773c50f3e0cc8842619a93edc4624525b728b188a9e0be33b7726adc5", 456_318),
        new("tokenizer/special_tokens_map.json", Sha256: "06e405a36dfe4b9604f484f6a1e619af1a7f7d09e34a8555eb0b77b66318067f", 280),
        new("tokenizer/tokenizer.json", Sha256: "dc239041d98de27ffc3975473a1a23e3db4c937b23c138c38bbc66588bd247e5", 2_108_774),
        new("tokenizer/tokenizer_config.json", Sha256: "41a59cba3ee32bd67c4822f8a4d715b980ac7050d82fa9161f7ed1ad40817dc5", 494),
        new("tokenizer/vocab.json", Sha256: "ed19656ea1707df69134c4af35c8ceda2cc9860bf2c3495026153a133670ab5e", 798_293),
        new("tokenizer_2/special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer_2/spiece.model", Sha256: "d60acb128cf7b7f2536e8f38a5b18a05535c9e14c7a355904270e15b0945ea86", 791_656),
        new("tokenizer_2/tokenizer.json", Sha256: "d7af4599a1914d04aaf44839418757f40ccca4c78033edeba369484978378335", 2_422_164),
        new("tokenizer_2/tokenizer_config.json", Sha256: "a02a72019d4f2b43d4f3b438c653051f846ceab9fa71d8dbaa04aed12c62cd56", 2_349),
        new("unet/config.json", Sha256: "c5fbbbbe20b03c44e59c43176f806a28ff16c0c199f142fafb2173a50d09a23d", 1_514),
        new("unet/diffusion_pytorch_model.safetensors", Sha256: "c7c710e8f17631b6b04dd7d987a906a4ed26f7a699f7a50c11e90910958b081a", 1_387_964_784),
        new("vae/config.json", Sha256: "260633c192a61d95d6d6b61ccf34ffe28957a5cee87ccf1db5bdc07ea0f50f70", 559),
        new("vae/diffusion_pytorch_model.safetensors", Sha256: "5f8ddddc5c45eddaab38a67a434e8a64486964540ba3fc248a0da7cbd599d4ad", 221_530_308),
        new("vocoder/config.json", Sha256: "fbee5b8848af276ca526792091fa7bd7ce68941343be9accd6ca77013215c738", 654),
        new("vocoder/model.safetensors", Sha256: "d9dc6513c30a5b86c2497712690c04fe74b4aa79fdab6d490b34fcb4e24c590c", 221_079_092),
        new("model_index.json", Sha256: "b79d5e17a8ba73e1131c98daf52e9fab18f9dfa98648a13fbfe1ab79b5c0752e", 805),
    ]));

    /// <summary>AudioLDM 2 (350M UNet, 1.1B in all; text to any sound, music too): <c>cvssp/audioldm2</c> at c8e7e18, the diffusers layout (safetensors only); about 4.48 GB.</summary>
    public static readonly LocalMusicVariant Base = new("audioldm2", "audioldm2", 30, LocalMusicFiles.From("cvssp/audioldm2", "c8e7e189d324425c05c4c2f81214041ef4107983", "audioldm2",
    [
        new("feature_extractor/preprocessor_config.json", Sha256: "9739f58296aa6f9ac18008fd0150fb2649bc554985fbde86d0a4041c882ac753", 541),
        new("language_model/config.json", Sha256: "dfd666349774232748f24a9fcc71c1cec577af0b9a1a18846cedbc37bbbf87bb", 902),
        new("language_model/model.safetensors", Sha256: "925012ada53083b40604540406b53570066b6d218380af45dd426fa531b875fb", 497_772_432),
        new("projection_model/config.json", Sha256: "78d94ba74ecacd622e206e6d751810c7bebd46a70632c318ae159056f100058a", 173),
        new("projection_model/diffusion_pytorch_model.safetensors", Sha256: "8d4d8b1233e8193c784ac7c99aed9f76b66312a9ddfe8b1bbad68fe03dd71bde", 4_737_688),
        new("scheduler/scheduler_config.json", Sha256: "b6d79c593aa336438b0826c5c00273e3c773283f6df0f365d1e908a291ec5367", 507),
        new("text_encoder/config.json", Sha256: "8680493b8b6622bf44565aaa69c80a9da28be20577bba2ef14095c845fdb1d17", 801),
        new("text_encoder/model.safetensors", Sha256: "a4a47b4a637dd58e9edb7b64a06acf37328b7cc3eafb0b8a85df895cc9e45d09", 776_327_432),
        new("text_encoder_2/config.json", Sha256: "45c375b0b91e81ff41960cac9e9c7bef294ca637fee1e3c40a8c12001190a0fd", 766),
        new("text_encoder_2/model.safetensors", Sha256: "c1d0c8f1c739db9343c12ea4b0e3f2c97a833b3c072c251e91d97b7326fefb4e", 1_364_951_064),
        new("tokenizer/merges.txt", Sha256: "1ce1664773c50f3e0cc8842619a93edc4624525b728b188a9e0be33b7726adc5", 456_318),
        new("tokenizer/special_tokens_map.json", Sha256: "06e405a36dfe4b9604f484f6a1e619af1a7f7d09e34a8555eb0b77b66318067f", 280),
        new("tokenizer/tokenizer.json", Sha256: "dc239041d98de27ffc3975473a1a23e3db4c937b23c138c38bbc66588bd247e5", 2_108_774),
        new("tokenizer/tokenizer_config.json", Sha256: "41a59cba3ee32bd67c4822f8a4d715b980ac7050d82fa9161f7ed1ad40817dc5", 494),
        new("tokenizer/vocab.json", Sha256: "ed19656ea1707df69134c4af35c8ceda2cc9860bf2c3495026153a133670ab5e", 798_293),
        new("tokenizer_2/special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer_2/spiece.model", Sha256: "d60acb128cf7b7f2536e8f38a5b18a05535c9e14c7a355904270e15b0945ea86", 791_656),
        new("tokenizer_2/tokenizer.json", Sha256: "d7af4599a1914d04aaf44839418757f40ccca4c78033edeba369484978378335", 2_422_164),
        new("tokenizer_2/tokenizer_config.json", Sha256: "a02a72019d4f2b43d4f3b438c653051f846ceab9fa71d8dbaa04aed12c62cd56", 2_349),
        new("unet/config.json", Sha256: "c5fbbbbe20b03c44e59c43176f806a28ff16c0c199f142fafb2173a50d09a23d", 1_514),
        new("unet/diffusion_pytorch_model.safetensors", Sha256: "359a5ffb89a844beb2fcfac584aae2cd7cd6e87c3ab1ec4e892ef45d91db77c2", 1_387_964_784),
        new("vae/config.json", Sha256: "260633c192a61d95d6d6b61ccf34ffe28957a5cee87ccf1db5bdc07ea0f50f70", 559),
        new("vae/diffusion_pytorch_model.safetensors", Sha256: "5f8ddddc5c45eddaab38a67a434e8a64486964540ba3fc248a0da7cbd599d4ad", 221_530_308),
        new("vocoder/config.json", Sha256: "fbee5b8848af276ca526792091fa7bd7ce68941343be9accd6ca77013215c738", 654),
        new("vocoder/model.safetensors", Sha256: "d9dc6513c30a5b86c2497712690c04fe74b4aa79fdab6d490b34fcb4e24c590c", 221_079_092),
        new("model_index.json", Sha256: "b79d5e17a8ba73e1131c98daf52e9fab18f9dfa98648a13fbfe1ab79b5c0752e", 805),
    ]));

    /// <summary>AudioLDM 2 Large (750M UNet, 1.5B in all; text to any sound): <c>cvssp/audioldm2-large</c> at 4b0b875, the diffusers layout (safetensors only); about 5.97 GB.</summary>
    public static readonly LocalMusicVariant Large = new("audioldm2-large", "audioldm2-large", 20, LocalMusicFiles.From("cvssp/audioldm2-large", "4b0b875a9e0c5305dfc917da808584e50e1c7ed4", "audioldm2-large",
    [
        new("feature_extractor/preprocessor_config.json", Sha256: "9739f58296aa6f9ac18008fd0150fb2649bc554985fbde86d0a4041c882ac753", 541),
        new("language_model/config.json", Sha256: "dfd666349774232748f24a9fcc71c1cec577af0b9a1a18846cedbc37bbbf87bb", 902),
        new("language_model/model.safetensors", Sha256: "350bb51cf4f23502f239009d12ce0230d166f9d3f5752196505c0a6d841a5514", 497_772_432),
        new("projection_model/config.json", Sha256: "78d94ba74ecacd622e206e6d751810c7bebd46a70632c318ae159056f100058a", 173),
        new("projection_model/diffusion_pytorch_model.safetensors", Sha256: "d41d9363c1e9f7c455d1ce9d60d3c888793e295128a1bc722be96b9f5718a4ed", 4_737_688),
        new("scheduler/scheduler_config.json", Sha256: "b6d79c593aa336438b0826c5c00273e3c773283f6df0f365d1e908a291ec5367", 507),
        new("text_encoder/config.json", Sha256: "8680493b8b6622bf44565aaa69c80a9da28be20577bba2ef14095c845fdb1d17", 801),
        new("text_encoder/model.safetensors", Sha256: "a4a47b4a637dd58e9edb7b64a06acf37328b7cc3eafb0b8a85df895cc9e45d09", 776_327_432),
        new("text_encoder_2/config.json", Sha256: "45c375b0b91e81ff41960cac9e9c7bef294ca637fee1e3c40a8c12001190a0fd", 766),
        new("text_encoder_2/model.safetensors", Sha256: "c1d0c8f1c739db9343c12ea4b0e3f2c97a833b3c072c251e91d97b7326fefb4e", 1_364_951_064),
        new("tokenizer/merges.txt", Sha256: "1ce1664773c50f3e0cc8842619a93edc4624525b728b188a9e0be33b7726adc5", 456_318),
        new("tokenizer/special_tokens_map.json", Sha256: "06e405a36dfe4b9604f484f6a1e619af1a7f7d09e34a8555eb0b77b66318067f", 280),
        new("tokenizer/tokenizer.json", Sha256: "dc239041d98de27ffc3975473a1a23e3db4c937b23c138c38bbc66588bd247e5", 2_108_774),
        new("tokenizer/tokenizer_config.json", Sha256: "41a59cba3ee32bd67c4822f8a4d715b980ac7050d82fa9161f7ed1ad40817dc5", 494),
        new("tokenizer/vocab.json", Sha256: "ed19656ea1707df69134c4af35c8ceda2cc9860bf2c3495026153a133670ab5e", 798_293),
        new("tokenizer_2/special_tokens_map.json", Sha256: "5c87151ef0f72a99d1f766a4c418bd2a1f90aaa30a8e22fe5eca9641daebb64f", 2_201),
        new("tokenizer_2/spiece.model", Sha256: "d60acb128cf7b7f2536e8f38a5b18a05535c9e14c7a355904270e15b0945ea86", 791_656),
        new("tokenizer_2/tokenizer.json", Sha256: "d7af4599a1914d04aaf44839418757f40ccca4c78033edeba369484978378335", 2_422_164),
        new("tokenizer_2/tokenizer_config.json", Sha256: "a02a72019d4f2b43d4f3b438c653051f846ceab9fa71d8dbaa04aed12c62cd56", 2_349),
        new("unet/config.json", Sha256: "973744c4fbd99018233df31b20c3f42160b7b6e81762448ab7a06f91f1b0b96e", 1_562),
        new("unet/diffusion_pytorch_model.safetensors", Sha256: "c58b1585fc87e9fb31cb16e049100c3626685ab418a4a1f48a370713f0131d13", 2_872_468_672),
        new("vae/config.json", Sha256: "5a90fafaac1c66a662c1dc8f0b103384d6df628d616ff44b74514a18ea075aee", 558),
        new("vae/diffusion_pytorch_model.safetensors", Sha256: "5f8ddddc5c45eddaab38a67a434e8a64486964540ba3fc248a0da7cbd599d4ad", 221_530_308),
        new("vocoder/config.json", Sha256: "fbee5b8848af276ca526792091fa7bd7ce68941343be9accd6ca77013215c738", 654),
        new("vocoder/model.safetensors", Sha256: "d9dc6513c30a5b86c2497712690c04fe74b4aa79fdab6d490b34fcb4e24c590c", 221_079_092),
        new("model_index.json", Sha256: "b79d5e17a8ba73e1131c98daf52e9fab18f9dfa98648a13fbfe1ab79b5c0752e", 805),
    ]));

    /// <summary>Every AudioLDM 2 model, in the catalog's order.</summary>
    public static readonly IReadOnlyList<LocalMusicVariant> All = [Music, Base, Large];
}
