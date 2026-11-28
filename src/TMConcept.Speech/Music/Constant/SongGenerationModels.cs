using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The SongGeneration (LeVo) models (28 Nov 2026; Tencent AI Lab: lyrics to a whole song with vocals and accompaniment, 48 kHz stereo; an
/// empty text makes an instrumental piece). Six released models: 1.0 base (Chinese, 2:30), base-new (Chinese and English, 2:30), base-full
/// and large (4:30), SongGeneration 2 (LeVo 2) large and medium (multilingual, 4:30); "v2-fast" was never released. None fits a 6 GB card:
/// the official README's table gives 10 GB for base and base-new, 12 GB for base-full and v2-medium, 22 GB for large and v2-large, and these
/// are already its low-memory (offload) figures, which the app always uses. Code and weights are under Tencent's "License Terms of
/// SongGeneration" (academic, research and education only: non-commercial, the orange warning and the one-time question). The weights are
/// lglg666's (the uploader the official README links to), not gated. Each model is its <c>model.pt</c> with its <c>config.yaml</c> in a folder
/// named as the code expects (<c>songgeneration_base</c>…: generate.py picks the version by that name); every model needs the shared runtime
/// (<c>lglg666/SongGeneration-Runtime</c>: the audio tokenizers, the VAE, content-vec, the Qwen2 tokenizer files and the demucs and
/// stable-audio-tools code it imports, about 14.5 GB), downloaded once into <c>runtime/</c>; demucs's own weights are left out (only a prompt
/// audio uses them, and the app sends none). Pins: Git LFS ids from the Hugging Face API, every small file measured on 28 Nov 2026.
/// </summary>
public static class SongGenerationModels
{
    /// <summary>The code path of the 1.0 models, as the script is told (<c>--version</c>).</summary>
    public const string Version1Code = "v1";

    /// <summary>The code path of the SongGeneration 2 models.</summary>
    public const string Version2Code = "v2";

    /// <summary>The folder of the shared runtime.</summary>
    public const string RuntimeFolder = "runtime";

    /// <summary>The shared runtime: <c>lglg666/SongGeneration-Runtime</c> at cc258cc (95 files, about 14.54 GB).</summary>
    public static readonly IReadOnlyList<ToolArchive> Runtime = LocalMusicFiles.From("lglg666/SongGeneration-Runtime", "cc258cc694a63114c61684cc26d0583b8ad777d0", RuntimeFolder,
    [
        new("ckpt/encode-s12k.pt", Sha256: "e250df56b035f74c1f66f15133f4c78f664d70fa0b09aa9a752b7871bb58c02f", 3_957_949_089),
        new("ckpt/model_1rvq/model_2_fixed.safetensors", Sha256: "339a16956b859a82defc02bfd32c3744d11ff942065f6ec9306dfd4400d62110", 4_704_507_596),
        new("ckpt/model_septoken/model_2.safetensors", Sha256: "430b7c1c245722fbe3893cd621b3d4a90076404596e9fb1ce987a4a0f2a4fc6f", 4_808_167_708),
        new("ckpt/models--lengyue233--content-vec-best/snapshots/c0b9ba13db21beaa4053faae94c102ebe326fd68/pytorch_model.bin", Sha256: "d8dd400e054ddf4e6be75dab5a2549db748cc99e756a097c496c099f65a4854e", 378_342_945),
        new("ckpt/vae/autoencoder_music_1320k.ckpt", Sha256: "10ccb6c83613781ad32e998a90597ba7eb9292911a224598da1fd53728eb4cd3", 674_920_616),
        new("ckpt/models--lengyue233--content-vec-best/refs/main", Sha256: "819e16afac87b230b4126b4c2b16d9bed88da9aff9dd61ce490962153ac73a34", 40),
        new("ckpt/models--lengyue233--content-vec-best/snapshots/c0b9ba13db21beaa4053faae94c102ebe326fd68/config.json", Sha256: "2ddde063b795d38d9051a7215a092fecf4cfe148b54251e38de51d88d356898b", 1_388),
        new("ckpt/vae/stable_audio_1920_vae.json", Sha256: "5cd2859efe00bc2b0f6f9bdac738ad11822a36473d6d810427b60efd057c538b", 3_613),
        new("third_party/Qwen2-7B/LICENSE", Sha256: "832dd9e00a68dd83b3c3fb9f5588dad7dcf337a0db50f7d9483f310cd292e92e", 11_343),
        new("third_party/Qwen2-7B/README.md", Sha256: "310d17f6409b7ccfadc402d5ab7157151cdcb99706043248fe06a6ca5bd27f41", 4_601),
        new("third_party/Qwen2-7B/config.json", Sha256: "d318b49c4a29771e259d93053084eb752f5bfe69c20c4f2e041bd1017884f810", 664),
        new("third_party/Qwen2-7B/generation_config.json", Sha256: "8c970692323e3ea0e9b8b0a4dca79388d31226e41f83c9fd6014804280ebf6e8", 138),
        new("third_party/Qwen2-7B/merges.txt", Sha256: "599bab54075088774b1733fde865d5bd747cbcc7a547c5bc12610e874e26f5e3", 1_671_839),
        new("third_party/Qwen2-7B/tokenizer.json", Sha256: "f7c9b2dba4a296b1aa76c16a34b8225c0c118978400d4bb66bff0902d702f5b8", 7_028_015),
        new("third_party/Qwen2-7B/tokenizer_config.json", Sha256: "80d64fccf954f022eb43b7425eb45a133cbb92c76a5f24d4cea6dbe3d45548d9", 1_289),
        new("third_party/Qwen2-7B/vocab.json", Sha256: "ca10d7e9fb3ed18575dd1e277a2579c16d108e32f27439684afa0e10b1440910", 2_776_833),
        new("third_party/demucs/models/apply.py", Sha256: "55b073faa5eb1c384ea2a04e91f30b642ebd0f2c8140e8c5ead87a59f2f8922b", 12_367),
        new("third_party/demucs/models/audio.py", Sha256: "a46736b2b8cd0e92bb0767bfecc2735d7203735799bbdbf5a5c45c7ade5925c3", 10_035),
        new("third_party/demucs/models/demucs.py", Sha256: "d7797e379803b76b5c0b8cf6df9908a5786ac62637c4f9613e4821e974ad238a", 17_020),
        new("third_party/demucs/models/htdemucs.py", Sha256: "c1c3e5830dbad6bdeb7dab197309562357cd20684595ea98fba92ead6122c80d", 37_020),
        new("third_party/demucs/models/pretrained.py", Sha256: "e1bc458e45753b7e85775361bd2fe6cb74cb774ca42301449c1329f29a70aabe", 1_076),
        new("third_party/demucs/models/spec.py", Sha256: "6fbf2b3e72fb317c2f5cb0b45b5fa8fd6cc5fde0b88310f2544f265e795d41b1", 1_385),
        new("third_party/demucs/models/states.py", Sha256: "c48464b347684b570856e69ecdba2207a7618406a03e97388a16c20bcd318145", 4_039),
        new("third_party/demucs/models/transformer.py", Sha256: "e1578bb5d132ad744593c654724828ccffca1848c54c01c0cb6b4f77c6666cec", 25_022),
        new("third_party/demucs/models/utils.py", Sha256: "f5bdf63d89dbc2608746bc1d828459ca5d6b308b09addbc015fe01c5b0f607ca", 3_568),
        new("third_party/demucs/run.py", Sha256: "b42761ad3fc2e3a45e65cf4e4dee622dde1eb693af5c42b391f08fc3b526e84e", 4_053),
        new("third_party/hub/version.txt", Sha256: "6b86b273ff34fce19d6b804eff5a3f5747ada4eaa22f1d49c01e52ddb7875b4b", 1),
        new("third_party/stable_audio_tools/LICENSE", Sha256: "a1fac33b7bcd791b74fb33aeb439f825e7277e239fc119fb7d2ab6f084a0c101", 1_069),
        new("third_party/stable_audio_tools/LICENSES/LICENSE_ADP.txt", Sha256: "9274c13f05c4ebdc741c501241315452c277626c31f9724d33cb15e7dae6b7db", 1_067),
        new("third_party/stable_audio_tools/LICENSES/LICENSE_AURALOSS.txt", Sha256: "43070e2d4e532684de521b885f385d0841030efa2b1a20bafb76133a5e1379c1", 11_356),
        new("third_party/stable_audio_tools/LICENSES/LICENSE_DESCRIPT.txt", Sha256: "3fb4aef5b76dc0ccb1f57499d75c2fe039df5c9793778ede172f5ac7a4045681", 1_073),
        new("third_party/stable_audio_tools/LICENSES/LICENSE_META.txt", Sha256: "cf9b17822d1fcd4ff32ccbe14183386fb3adf6f2ff92dc184130823f7fc28173", 1_087),
        new("third_party/stable_audio_tools/LICENSES/LICENSE_NVIDIA.txt", Sha256: "da9858d516047d82096d01c112a61bd67f26d289039464d668a1d45f91738ecc", 1_076),
        new("third_party/stable_audio_tools/LICENSES/LICENSE_XTRANSFORMERS.txt", Sha256: "bc3384ef4c712fc7bca78a9605289296a57d812240c9cbef1e49b0191eb74982", 1_065),
        new("third_party/stable_audio_tools/README.md", Sha256: "b0baf25499e96f79bd05febc0e00c61a0947d533fbb402b148290aeaa15c6d64", 7_175),
        new("third_party/stable_audio_tools/config/model_1920.json", Sha256: "5cd2859efe00bc2b0f6f9bdac738ad11822a36473d6d810427b60efd057c538b", 3_613),
        new("third_party/stable_audio_tools/config/model_config.json", Sha256: "54efcf70523bfd07ddf411aef46ccc238b9e66a76bac0f384f1ab4278b6a2b57", 3_611),
        new("third_party/stable_audio_tools/defaults.ini", Sha256: "801df84e61cf8b5f28cd9ca7b96c0129d8229e7654930e354d255890f650bf90", 1_155),
        new("third_party/stable_audio_tools/pyproject.toml", Sha256: "1c557fcb2d563b509abf60fdb4132d72373474dbf6d059d752b2b780788d43ed", 80),
        new("third_party/stable_audio_tools/run_gradio.py", Sha256: "a89900c14e03b5b8f05ce824231f3d643eafcc3feec4ae305187cf4cdd78ebc6", 1_573),
        new("third_party/stable_audio_tools/scripts/ds_zero_to_pl_ckpt.py", Sha256: "95022ac29b1a7db4200fcab4a5f96214bab401760adf2ac7f4c24e84d58a18cb", 617),
        new("third_party/stable_audio_tools/setup.py", Sha256: "6e1a0bf1bbff25751d3d34fbace20b075c9df6cbeb8c97f6d183b2df4a34786a", 1_290),
        new("third_party/stable_audio_tools/stable_audio_tools/__init__.py", Sha256: "76bea5faebc1b46c1b3c160ae5038cf13afd1baeb7ce4e115a1b7dd62d9b91db", 135),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/dataset_configs/custom_metadata/custom_md_example.py", Sha256: "b8d334ee3ca7c27877dab0e7ac5f6350eb0e02bd72e784cb88f90af4674cd299", 115),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/dataset_configs/local_training_example.json", Sha256: "e47dc989fe81fcbb6595ad5099f98b091f42caeabb0bcdff07bdb7591e078e63", 232),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/dataset_configs/s3_wds_example.json", Sha256: "b8ca72f9212bf1b2132dbef7dfe17bbc439bb49fa67049077541c2237c25ed3e", 194),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/model_configs/autoencoders/dac_2048_32_vae.json", Sha256: "d1936597dbba91af969400f0106d7e708e2ec667c55f838d1ee6237b0c42d055", 1_994),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/model_configs/autoencoders/encodec_musicgen_rvq.json", Sha256: "aee7aa68eaec5359d857673214c29eab01fe9e285d072249796813055076e6d3", 2_541),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/model_configs/autoencoders/stable_audio_1_0_vae.json", Sha256: "e38dbf75f087fd4906b5f5806a0e187194ba848892fecb5318d50b695b6c2dd3", 3_146),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/model_configs/autoencoders/stable_audio_2_0_vae.json", Sha256: "eb6e598d22386492cc71f76d570edb126884742548679e3cfd4acfecc321f97f", 3_611),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/model_configs/autoencoders/stable_audio_vae_1920.json", Sha256: "a227bec26b5bad606f1c2c23eb12d54c430d5732c44bbf640798a7e428a35207", 3_414),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/model_configs/dance_diffusion/dance_diffusion_base.json", Sha256: "173c0ed31509cd20b5217e18d30773b8becc0e4a97673630a152d1d67f814f31", 346),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/model_configs/dance_diffusion/dance_diffusion_base_16k.json", Sha256: "4a335ded456f0d84804845ac2881363ed8b817a22733ba1ec0857f7cff4beee9", 346),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/model_configs/dance_diffusion/dance_diffusion_base_44k.json", Sha256: "fbe96c07696de5f6f635913fe69a1383f07ea854137bf4596e86addc66f9f38b", 346),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/model_configs/dance_diffusion/dance_diffusion_large.json", Sha256: "b9c108a427af923c192f035434666ea50189304c40a7815ea900848874ed070e", 347),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/model_configs/txt2audio/stable_audio_1_0.json", Sha256: "3d89760427f603ab77532d33455273499f4bdb56741ba0ac8249be2ed0598d9a", 3_878),
        new("third_party/stable_audio_tools/stable_audio_tools/configs/model_configs/txt2audio/stable_audio_2_0.json", Sha256: "baf29dd8f37ad71d8e012f0d02e9c0cda98a7d120627096bce68b2e70bbf911e", 4_515),
        new("third_party/stable_audio_tools/stable_audio_tools/data/dataset.py", Sha256: "9b9f85e32a1c325f8beff623fcff1de4e17b3c067725116a756051daf247266e", 24_442),
        new("third_party/stable_audio_tools/stable_audio_tools/data/utils.py", Sha256: "3822b20bfd6808c4f30253f1ff3f8c87a65164f59aa3db05c5135298aedbf346", 3_235),
        new("third_party/stable_audio_tools/stable_audio_tools/inference/generation.py", Sha256: "4d007e847b8defa4ffbcbef68d0e5c5f83623fb8d59dc26308f7e875ea841ae8", 12_090),
        new("third_party/stable_audio_tools/stable_audio_tools/inference/sampling.py", Sha256: "ebecba64106701292e6af9fa6043f50b56ba6061a28cd8a253c68db7dea2a40e", 9_445),
        new("third_party/stable_audio_tools/stable_audio_tools/inference/utils.py", Sha256: "ae13fe3ef579ebca4b2f3f4053e4e7563dce33c7e67a6b53feb6f8ebaf2d7f41", 979),
        new("third_party/stable_audio_tools/stable_audio_tools/interface/gradio.py", Sha256: "c175b2d34343d98b478d7966ad9e299fafd9ba22ae226997e7905ed8b6076da1", 27_040),
        new("third_party/stable_audio_tools/stable_audio_tools/models/__init__.py", Sha256: "a2b31933f3dc05807497a1454438a0231563c0c098e94373e08d958395324ffb", 76),
        new("third_party/stable_audio_tools/stable_audio_tools/models/adp.py", Sha256: "16d184258b1ca6ba1636deb247f238e9998a324d50a10e6d8a1a161dd8f69581", 55_413),
        new("third_party/stable_audio_tools/stable_audio_tools/models/autoencoders.py", Sha256: "55754f49d228adaea63154130d23e47c285eed8cdee522acfa74173a921af62b", 32_162),
        new("third_party/stable_audio_tools/stable_audio_tools/models/blocks.py", Sha256: "29c80107970182ae09ee0c824a52970e0352a4b994da1dd03fd475831f1666b5", 11_526),
        new("third_party/stable_audio_tools/stable_audio_tools/models/bottleneck.py", Sha256: "17a89b11455cb267cccbdba751c751e39dcf8b4f58cd261a5c2abf32c77426f7", 10_450),
        new("third_party/stable_audio_tools/stable_audio_tools/models/codebook_patterns.py", Sha256: "0cceee78ad20c49eed3e6e5a79039f5f944fb15544658626b1610b430beea0b6", 28_208),
        new("third_party/stable_audio_tools/stable_audio_tools/models/conditioners.py", Sha256: "5163563df9a8eca3d7044c2c5ab581ea6d658694c1bc01bbdd89ab0aad2220ec", 21_455),
        new("third_party/stable_audio_tools/stable_audio_tools/models/diffusion.py", Sha256: "51f0d8d7e303f3f55b21d40d984d59ec06795c3d03c787f8d52afb4c8453ad54", 24_854),
        new("third_party/stable_audio_tools/stable_audio_tools/models/diffusion_prior.py", Sha256: "0874c73e027034cde94992694c073a33107aaede56ebdde153e5b9a213edc5c7", 2_542),
        new("third_party/stable_audio_tools/stable_audio_tools/models/discriminators.py", Sha256: "48f705381431d0d75114a3cf7fcb3752b38e8c126c27324788266ad4def74b1d", 17_583),
        new("third_party/stable_audio_tools/stable_audio_tools/models/dit.py", Sha256: "2d4b80c7416f0e828ba7322f3f7c8f8d58a90a037c0070179ddd1a678e9bb1ab", 14_700),
        new("third_party/stable_audio_tools/stable_audio_tools/models/factory.py", Sha256: "7409b3f4b790d82e838344dff531f2e2399eeea8139297d9b325f878b415f44a", 6_303),
        new("third_party/stable_audio_tools/stable_audio_tools/models/lm.py", Sha256: "5abcb0b9ec11601b9dc3d694e84c465a3a283f1f21c977a1613996dacfcbe9aa", 21_578),
        new("third_party/stable_audio_tools/stable_audio_tools/models/lm_backbone.py", Sha256: "26548d6db3274e96cd4b089388add6fe78c8ddc024456b94f76a881c69775c6d", 5_929),
        new("third_party/stable_audio_tools/stable_audio_tools/models/local_attention.py", Sha256: "c3b40d3844f365053296dca0763ec6f2c4415019b0ec44152ce221ecf77b13db", 8_289),
        new("third_party/stable_audio_tools/stable_audio_tools/models/pqmf.py", Sha256: "7e2fc9cd35dd474ba266bc744e0ed5694cc044ebf40522598381e72a5099ac3b", 13_238),
        new("third_party/stable_audio_tools/stable_audio_tools/models/pretrained.py", Sha256: "92629241d4a76aa43af4acf59743512dce789e129a17dee35e7b358ec02d4de0", 837),
        new("third_party/stable_audio_tools/stable_audio_tools/models/pretransforms.py", Sha256: "eb70ee1d2619c97777079a48346136e1504136a6b74e4dd08641b57fb8db0dd6", 8_436),
        new("third_party/stable_audio_tools/stable_audio_tools/models/transformer.py", Sha256: "393e735d65a5adec454ab85949ea806f3ee3c13772437ff43fd095abfd32fb46", 26_493),
        new("third_party/stable_audio_tools/stable_audio_tools/models/utils.py", Sha256: "8a2d5c2ebe1d2016ac45100281585894ab502237c167a5a3841d64ac77d45a39", 3_403),
        new("third_party/stable_audio_tools/stable_audio_tools/models/wavelets.py", Sha256: "f3b0f9db0ee935ee918daa8bf8007862020e0e6097530495cec17c48846bfccf", 3_240),
        new("third_party/stable_audio_tools/stable_audio_tools/training/__init__.py", Sha256: "e7b95484070b7565316bfd867a76a5b68b57ddbf773dfed4512d5f12e96d2b29", 91),
        new("third_party/stable_audio_tools/stable_audio_tools/training/autoencoders.py", Sha256: "af3208d4bb8b38f8b1654a80599d67b6895d488bc1c6d8cda7c4d4c11207a32e", 18_386),
        new("third_party/stable_audio_tools/stable_audio_tools/training/diffusion.py", Sha256: "2397da5ff2fb318ac3d53a602831d8ab59adcca84bcb62ea991c467f54f2d83e", 56_542),
        new("third_party/stable_audio_tools/stable_audio_tools/training/factory.py", Sha256: "b5b0b21a77da41a5ef38a83afbf3a00ff9f21373b16b8528e42df156d5a91242", 10_882),
        new("third_party/stable_audio_tools/stable_audio_tools/training/lm.py", Sha256: "45b6263dfece619e1691917a49732e4585608458463dfd407fd18fcae7307443", 10_075),
        new("third_party/stable_audio_tools/stable_audio_tools/training/losses/__init__.py", Sha256: "b9ee97e89e05f6dbbe4b10207be8f24e76422784f6f4a64fb040ca7e805ec276", 21),
        new("third_party/stable_audio_tools/stable_audio_tools/training/losses/auraloss.py", Sha256: "3956f1fe0805fd5784e391e1697af28e358f2dc568c6f5e31dc07515c0002cc8", 23_627),
        new("third_party/stable_audio_tools/stable_audio_tools/training/losses/losses.py", Sha256: "fbf0b24dec2112de2faef2b89d48772de91d39a102401d9f9892935fed221d37", 2_929),
        new("third_party/stable_audio_tools/stable_audio_tools/training/utils.py", Sha256: "e7b0139fce618762d5fe14bbf8147e2f7bb6ed2cbf97f8ed777e3a2e64ea5da2", 4_146),
        new("third_party/stable_audio_tools/train.py", Sha256: "b7758efd5a609fbc374b4a473f2a8fdde27c1a82f9401aee7f5e1def6f66215a", 4_804),
        new("third_party/stable_audio_tools/unwrap_model.py", Sha256: "c813cf16c6fc7589016c6313683c546c63e474b373bef61a28386c2ce587e2ef", 5_512),
    ]);

    /// <summary>SongGeneration base (Chinese, up to 2:30): <c>lglg666/SongGeneration-base</c> at 0730c18; 11.3 GB plus the runtime.</summary>
    public static readonly LocalMusicVariant Base = new("songgeneration-base", "songgeneration_base", 150, [.. LocalMusicFiles.From("lglg666/SongGeneration-base", "0730c1874831a6e7aca481be1fd9852c46be46cd", "songgeneration_base",
    [
        new("model.pt", Sha256: "8763fc75e5db768c334a9fbadd08e2004eccb6e15156c76b4c2a3984f8fbb884", 11_318_365_872),
        new("config.yaml", Sha256: "d846b27d83a9ba33648d51fc5a2db451a89100b66b7007a91d701dfaf3fdf603", 3_349),
    ]), .. Runtime], RuntimeFolder, [new LocalMusicOption("--version", Version1Code)]);

    /// <summary>SongGeneration base-new (Chinese and English, up to 2:30): <c>lglg666/SongGeneration-base-new</c> at 6f5da44; 11.3 GB plus the runtime.</summary>
    public static readonly LocalMusicVariant BaseNew = new("songgeneration-base-new", "songgeneration_base_new", 150, [.. LocalMusicFiles.From("lglg666/SongGeneration-base-new", "6f5da44b2ec38c5599d475d16cbe89c1af858284", "songgeneration_base_new",
    [
        new("model.pt", Sha256: "4ba918b85a34acc9a5fab564e5120eb8588add2d20337040f8a61fd1dbae46ca", 11_318_365_836),
        new("config.yaml", Sha256: "d846b27d83a9ba33648d51fc5a2db451a89100b66b7007a91d701dfaf3fdf603", 3_349),
    ]), .. Runtime], RuntimeFolder, [new LocalMusicOption("--version", Version1Code)]);

    /// <summary>SongGeneration base-full (Chinese and English, up to 4:30): <c>lglg666/SongGeneration-base-full</c> at 19ebdb6; 11.3 GB plus the runtime.</summary>
    public static readonly LocalMusicVariant BaseFull = new("songgeneration-base-full", "songgeneration_base_full", 270, [.. LocalMusicFiles.From("lglg666/SongGeneration-base-full", "19ebdb6090e4df5c1e211ffb9588f9c8d98c9279", "songgeneration_base_full",
    [
        new("model.pt", Sha256: "ee3a2f0a38b5f99ee82b7e08406b50389407caaf3749bd11007c27ea1b6e0806", 11_318_365_836),
        new("config.yaml", Sha256: "1932a4b1e8e7396d825e077155cfa7494739ab8f1ab455e8a6fba95902174878", 3_351),
    ]), .. Runtime], RuntimeFolder, [new LocalMusicOption("--version", Version1Code)]);

    /// <summary>SongGeneration large (Chinese and English, up to 4:30): <c>lglg666/SongGeneration-large</c> at 75e2043; 20.5 GB plus the runtime.</summary>
    public static readonly LocalMusicVariant Large = new("songgeneration-large", "songgeneration_large", 270, [.. LocalMusicFiles.From("lglg666/SongGeneration-large", "75e20435b1cc7f97339f453c84ae5cffba58f5ea", "songgeneration_large",
    [
        new("model.pt", Sha256: "cce9120765bae326d568fb39bf0cb20b6ccc983024ce42e358aa7e04532986d4", 20_489_347_676),
        new("config.yaml", Sha256: "6fbcb212f5bc98dc8a09a0a6be4aea3f1b13098c25a60b0672046f3c3428f641", 3_352),
    ]), .. Runtime], RuntimeFolder, [new LocalMusicOption("--version", Version1Code)]);

    /// <summary>
    /// SongGeneration 2 (LeVo 2) large, 4B (multilingual, up to 4:30): <c>lglg666/SongGeneration-v2-large</c> at 1158053 (the 9 Mar 2026
    /// checkpoint the README says to download again); 12.9 GB plus the runtime.
    /// </summary>
    public static readonly LocalMusicVariant V2Large = new("songgeneration-v2-large", "songgeneration_v2_large", 270, [.. LocalMusicFiles.From("lglg666/SongGeneration-v2-large", "115805364ad74479fb3764fe65970c92faeb1a5a", "songgeneration_v2_large",
    [
        new("model.pt", Sha256: "dc763aa9a76a22a87597c2faf9a51c24d13349ac754699b37e9068b483639def", 12_899_965_446),
        new("config.yaml", Sha256: "14a991bd7342b9dde348e6324afd44b5c6ecb1db8d0ed4d2dbe666b220b04c59", 3_352),
    ]), .. Runtime], RuntimeFolder, [new LocalMusicOption("--version", Version2Code)]);

    /// <summary>SongGeneration 2 (LeVo 2) medium (multilingual, up to 4:30): <c>lglg666/SongGeneration-v2-medium</c> at 7d91660; 7.3 GB plus the runtime.</summary>
    public static readonly LocalMusicVariant V2Medium = new("songgeneration-v2-medium", "songgeneration_v2_medium", 270, [.. LocalMusicFiles.From("lglg666/SongGeneration-v2-medium", "7d91660ebfa041e29bace194f5631e775796f600", "songgeneration_v2_medium",
    [
        new("model.pt", Sha256: "4ef2be41f6d838824f5432491408f68d9ffbeda3b1349e1208f9cdfcc64445b1", 7_343_951_582),
        new("config.yaml", Sha256: "9b1e1cb79b9824816e9f119e1d3e1c3dbb91265121fdd4219667f8e0158a563f", 3_351),
    ]), .. Runtime], RuntimeFolder, [new LocalMusicOption("--version", Version2Code)]);

    /// <summary>Every SongGeneration model, in the catalog's order.</summary>
    public static readonly IReadOnlyList<LocalMusicVariant> All = [Base, BaseNew, BaseFull, Large, V2Large, V2Medium];
}
