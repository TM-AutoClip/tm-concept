using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The DiffRhythm models (28 Nov 2026; ASLP-lab, NWPU: a 1.1B latent diffusion transformer that makes a whole piece at once, 44.1 kHz stereo).
/// The app makes instrumental pieces only: DiffRhythm's words need timestamped lyrics and the espeak-ng phonemizer, a system install on
/// Windows (GPL-3.0) the app does not do; without words its script never builds the phonemizer. Its style comes from the prompt through
/// MuQ-MuLan. A base model makes exactly 95 s, DiffRhythm 1.2 Full 96 to 285 s, DiffRhythm 1.0 Full exactly 285 s; a shorter piece is cut.
/// Licences: the transformers are Apache-2.0, but every model needs MuQ-MuLan (CC-BY-NC-4.0: non-commercial, the orange warning and the
/// one-time question) and DiffRhythm's VAE (Stability AI Community License). The files every model shares (the VAE, MuQ-MuLan and the MuQ and
/// XLM-RoBERTa weights its code builds itself from, about 5.74 GB) are downloaded once into <c>shared/</c>. None of the repositories is gated.
/// Pins: Git LFS ids from the Hugging Face API, small files measured on 28 Nov 2026.
/// <para>
/// Card classes: DiffRhythm's README (at the pinned commit) says "DiffRhythm-base requires a minimum of 8G of VRAM", with chunked decoding,
/// which the script uses: 8 GB for the base models. The full models make three times the frames (<see cref="FullFrames"/>) and have no
/// official figure: 12 GB.
/// </para>
/// </summary>
public static class DiffRhythmModels
{
    /// <summary>The folder of the shared files.</summary>
    public const string SharedFolder = "shared";

    /// <summary>The frames of a base model's piece (exactly 95 s).</summary>
    public const int BaseFrames = 2048;

    /// <summary>The frames of a full model's piece (up to 285 s).</summary>
    public const int FullFrames = 6144;

    /// <summary>The longest piece of a base model (seconds; exactly this).</summary>
    public const int BaseSeconds = 95;

    /// <summary>The files every DiffRhythm model needs (about 5.74 GB).</summary>
    public static readonly IReadOnlyList<ToolArchive> Shared =
    [
        .. LocalMusicFiles.From("ASLP-lab/DiffRhythm-vae", "74e2afacfd91dd1b96662c96dcef763c1258768b", SharedFolder + "/vae",
        [
            new("vae_model.pt", Sha256: "712693f27299937c6ccf1a6d6f1d9b45c7c8c11210d3b0cbb0f36181465ba29f", 624_520_127),
        ]),
        .. LocalMusicFiles.From("OpenMuQ/MuQ-MuLan-large", "2e01c796b71dca71b45251384c04cd7b237c9020", SharedFolder + "/muq-mulan",
        [
            new("pytorch_model.bin", Sha256: "d42ae3f7cb9b66759ee0089ddc70e2f28b130c2d8ba621457358272d32dd0444", 2_653_954_401),
            new("config.json", Sha256: "8fefc545ef87ecd9bcde7417dd03464370c48c321f36dcff20266a752079e468", 847),
        ]),
        .. LocalMusicFiles.From("OpenMuQ/MuQ-large-msd-iter", "0562a57814f6f8bbd9fdea0a25921a2fce1a841a", SharedFolder + "/muq-msd",
        [
            new("model.safetensors", Sha256: "273febab2be02872c37d2c37e48a9d6c52c1c9392f3eeeabd498efa281ccb7a6", 1_333_825_096),
            new("config.json", Sha256: "237335ee27d8fb951ce778701a12a79e06c51ae636dd786f97e45f51ce532543", 3_133),
        ]),
        .. LocalMusicFiles.From("FacebookAI/xlm-roberta-base", "e73636d4f797dec63c3081bb6ed5c7b0bb3f2089", SharedFolder + "/xlm-roberta-base",
        [
            new("model.safetensors", Sha256: "6fd4797bc397c3b8b55d6bb5740366b57e6a3ce91c04c77f22aafc0c128e6feb", 1_115_567_652),
            new("config.json", Sha256: "d66ed8cd4f2a93b358c245e50736fa389ed4f35c0bae7aad0b32abb20c62b579", 615),
            new("sentencepiece.bpe.model", Sha256: "cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865", 5_069_051),
            new("tokenizer.json", Sha256: "a898ea75433890f6610f4e470b8ebeb0c21dce5c8dd61f892eb09eb5919d2e2c", 9_096_718),
            new("tokenizer_config.json", Sha256: "994f46754c5bf4014f1aa92d34b1374319c3a6b3f702105cd5b742beaecd18ce", 25),
        ]),
    ];

    /// <summary>DiffRhythm 1.2 (95 s): <c>ASLP-lab/DiffRhythm-1_2</c> at 185bdeb; about 7.96 GB with the shared files.</summary>
    public static readonly LocalMusicVariant Base12 = new("diffrhythm-1.2-base", "diffrhythm-1.2-base", BaseSeconds, [.. LocalMusicFiles.From("ASLP-lab/DiffRhythm-1_2", "185bdeb80541b9260d266c5f041859017441f307", "diffrhythm-1.2-base",
    [
        new("cfm_model.pt", Sha256: "3e819b317ce2cf1fb22f386d74f351b697204ec1f57f03edfe50dbca71cf0768", 2_218_709_125),
    ]), .. Shared], SharedFolder);

    /// <summary>DiffRhythm 1.2 Full (96 to 285 s): <c>ASLP-lab/DiffRhythm-1_2-full</c> at 2aea17a; about 7.96 GB with the shared files.</summary>
    public static readonly LocalMusicVariant Full12 = new("diffrhythm-1.2-full", "diffrhythm-1.2-full", 285, [.. LocalMusicFiles.From("ASLP-lab/DiffRhythm-1_2-full", "2aea17a3cf7072e0933f15be3a01e762a5c7fbe9", "diffrhythm-1.2-full",
    [
        new("cfm_model.pt", Sha256: "72a1215fda7b4117eb5314c6a4f00b1f9a9d0ca7c89e19bae10b73246a8f4993", 2_219_500_168),
    ]), .. Shared], SharedFolder);

    /// <summary>DiffRhythm 1.0 (95 s, the older model): <c>ASLP-lab/DiffRhythm-base</c> at 6cb1176; about 7.96 GB with the shared files.</summary>
    public static readonly LocalMusicVariant Base10 = new("diffrhythm-1.0-base", "diffrhythm-1.0-base", BaseSeconds, [.. LocalMusicFiles.From("ASLP-lab/DiffRhythm-base", "6cb11765c53bd4c7548f40d9e43588e508c2ec6f", "diffrhythm-1.0-base",
    [
        new("cfm_model.pt", Sha256: "128a2513fca01531339cc5698c30e4f5a375256d1ed2257b464db9c886d6801a", 2_218_706_831),
    ]), .. Shared], SharedFolder);

    /// <summary>DiffRhythm 1.0 Full (exactly 285 s, the older model): <c>ASLP-lab/DiffRhythm-full</c> at 613846a; about 7.96 GB with the shared files.</summary>
    public static readonly LocalMusicVariant Full10 = new("diffrhythm-1.0-full", "diffrhythm-1.0-full", 285, [.. LocalMusicFiles.From("ASLP-lab/DiffRhythm-full", "613846abae8e5b869b3845a5dfabc9ecc37ecdab", "diffrhythm-1.0-full",
    [
        new("cfm_model.pt", Sha256: "d8f33d24107bfdcf98cf9b7c427800cc35a2b61023750518d3fff58e617f0a47", 2_218_708_010),
    ]), .. Shared], SharedFolder);

    /// <summary>The DiffRhythm 1.2 models, in the catalog's order.</summary>
    public static readonly IReadOnlyList<LocalMusicVariant> Current = [Base12, Full12];

    /// <summary>The DiffRhythm 1.0 models, in the catalog's order.</summary>
    public static readonly IReadOnlyList<LocalMusicVariant> Legacy = [Base10, Full10];
}
