using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The MAGNeT models (28 Nov 2026; Meta FAIR, masked non-autoregressive generation through audiocraft at a pinned commit,
/// <see cref="AudiocraftPins"/>). Four make music (32 kHz, mono), two (Audio-MAGNeT) make sound effects (16 kHz, mono); none sings. Each
/// checkpoint makes exactly its own length (10 or 30 s, no continuation); a longer video loops it. Meta's repositories under
/// <c>facebook/</c> are not gated, the weights CC-BY-NC-4.0 (non-commercial: the orange warning and the one-time question), the code MIT.
/// A checkpoint is its two audiocraft files (<c>state_dict.bin</c> = the language model, <c>compression_state_dict.bin</c> = the whole EnCodec,
/// so no other EnCodec download), plus the T5 text encoder audiocraft names (<c>t5-base</c> for music, <c>t5-large</c> for sound effects:
/// <c>config/conditioner/text2music.yaml</c> and <c>text2sound.yaml</c>), shared by every checkpoint that needs it. Pins: Git LFS ids from the
/// Hugging Face API, small files measured on 28 Nov 2026.
/// <para>
/// Card classes: the script runs audiocraft's own path (its language model in 16 bits on the card), the one AudioCraft's
/// <c>docs/MAGNET.md</c> means when it recommends a 16 GB card for the ~1.5B (medium) models: 16 GB for MAGNeT Medium and Audio-MAGNeT
/// Medium. The 300M (small) ones have no official figure: 4 GB.
/// </para>
/// </summary>
public static class MagnetModels
{
    /// <summary>
    /// T5-base (Apache-2.0), the text encoder of the music models: <c>google-t5/t5-base</c> at a9723ea, about 0.89 GB, downloaded
    /// once into <c>t5-base/</c>.
    /// </summary>
    public static readonly IReadOnlyList<ToolArchive> T5Base = LocalMusicFiles.From("google-t5/t5-base", "a9723ea7f1b39c1eae772870f3b547bf6ef7e6c1", "t5-base",
    [
        new("model.safetensors", Sha256: "a90903540cc02cbeb7ff9f823f1a80eb778c7e22426a0e620b01c77a5ec8f5b4", 891_646_390),
        new("config.json", Sha256: "46dd7cb62d29c81fb551e0ef1ea274c24a46ba441eeb948897706252933df033", 1_208),
        new("spiece.model", Sha256: "d60acb128cf7b7f2536e8f38a5b18a05535c9e14c7a355904270e15b0945ea86", 791_656),
    ]);

    /// <summary>
    /// T5-large (Apache-2.0), the text encoder of the sound effect models: <c>google-t5/t5-large</c> at 150ebc2, about 2.95 GB, downloaded
    /// once into <c>t5-large/</c>.
    /// </summary>
    public static readonly IReadOnlyList<ToolArchive> T5Large = LocalMusicFiles.From("google-t5/t5-large", "150ebc2c4b72291e770f58e6057481c8d2ed331a", "t5-large",
    [
        new("model.safetensors", Sha256: "bb566a699a6939ffa2ac2fa7eb26fd02b13d4d14956186d7af1e46ce19c67d32", 2_950_736_730),
        new("config.json", Sha256: "3ae9083d31e82430ac5ffaf87af48e4a39758c8911d33837b0d01d423fc50eeb", 1_209),
        new("spiece.model", Sha256: "d60acb128cf7b7f2536e8f38a5b18a05535c9e14c7a355904270e15b0945ea86", 791_656),
    ]);

    /// <summary>MAGNeT Small, 10 s music (300M): <c>facebook/magnet-small-10secs</c> at 2c90847 with T5-base; about 1.97 GB with the text encoder.</summary>
    public static readonly LocalMusicVariant Small10 = new("magnet-small-10secs", "magnet-small-10secs", 10, [.. LocalMusicFiles.From("facebook/magnet-small-10secs", "2c9084771bd2e83c5c7e36303e24550da30da8e0", "magnet-small-10secs",
    [
        new("state_dict.bin", Sha256: "0594e551ed9c40464b5918f5ddcce348e491e912e61d69f4d5d64d4ddd1a6ade", 840_844_851),
        new("compression_state_dict.bin", Sha256: "91598c7da3d183eb8e0cc19cbbdc4f64f2d0c53069f9c8aa84185d0e33873c67", 236_003_715),
    ]), .. T5Base], "t5-base");

    /// <summary>MAGNeT Small, 30 s music (300M): <c>facebook/magnet-small-30secs</c> at 7d62a3e with T5-base; about 1.97 GB with the text encoder.</summary>
    public static readonly LocalMusicVariant Small30 = new("magnet-small-30secs", "magnet-small-30secs", 30, [.. LocalMusicFiles.From("facebook/magnet-small-30secs", "7d62a3ef839d51abb5f087ac72732d6f45301460", "magnet-small-30secs",
    [
        new("state_dict.bin", Sha256: "965b4d537d691483918ccc6d97e738c0177191900c2e2cd3272d29fa3c9a44d0", 840_844_851),
        new("compression_state_dict.bin", Sha256: "91598c7da3d183eb8e0cc19cbbdc4f64f2d0c53069f9c8aa84185d0e33873c67", 236_003_715),
    ]), .. T5Base], "t5-base");

    /// <summary>MAGNeT Medium, 10 s music (1.5B): <c>facebook/magnet-medium-10secs</c> at 3a112c2 with T5-base; about 4.81 GB with the text encoder.</summary>
    public static readonly LocalMusicVariant Medium10 = new("magnet-medium-10secs", "magnet-medium-10secs", 10, [.. LocalMusicFiles.From("facebook/magnet-medium-10secs", "3a112c2c343d839b45137cf30be63dec09c3a237", "magnet-medium-10secs",
    [
        new("state_dict.bin", Sha256: "6a7ed21fdfaf6360a71d70adea45ad48a034ce94fb38df047d1e4edafad59ae0", 3_677_670_163),
        new("compression_state_dict.bin", Sha256: "91598c7da3d183eb8e0cc19cbbdc4f64f2d0c53069f9c8aa84185d0e33873c67", 236_003_715),
    ]), .. T5Base], "t5-base");

    /// <summary>MAGNeT Medium, 30 s music (1.5B): <c>facebook/magnet-medium-30secs</c> at 2559c59 with T5-base; about 4.81 GB with the text encoder.</summary>
    public static readonly LocalMusicVariant Medium30 = new("magnet-medium-30secs", "magnet-medium-30secs", 30, [.. LocalMusicFiles.From("facebook/magnet-medium-30secs", "2559c5978450f62782cf1d17826d384fb93fb64b", "magnet-medium-30secs",
    [
        new("state_dict.bin", Sha256: "9bc89122b640f11394f51e6e77b4194d57da328ae81b2301ee316de916dbf4c5", 3_677_670_163),
        new("compression_state_dict.bin", Sha256: "91598c7da3d183eb8e0cc19cbbdc4f64f2d0c53069f9c8aa84185d0e33873c67", 236_003_715),
    ]), .. T5Base], "t5-base");

    /// <summary>Audio-MAGNeT Small, 10 s sound effects (300M): <c>facebook/audio-magnet-small</c> at 7d3fab1 with T5-large; about 4.03 GB with the text encoder.</summary>
    public static readonly LocalMusicVariant AudioSmall = new("audio-magnet-small", "audio-magnet-small", 10, [.. LocalMusicFiles.From("facebook/audio-magnet-small", "7d3fab110310dd3b3dd05e7cd38e9cf9f4a10e71", "audio-magnet-small",
    [
        new("state_dict.bin", Sha256: "58b0c04add7766a550430edb83c8b6f4c04539446120c8dd2a6118636b81943b", 841_369_459),
        new("compression_state_dict.bin", Sha256: "776320589e3cefc0938f1b3105f19b6b11faeef38478b48ea6f7ce48112189f1", 235_742_595),
    ]), .. T5Large], "t5-large");

    /// <summary>Audio-MAGNeT Medium, 10 s sound effects (1.5B): <c>facebook/audio-magnet-medium</c> at d61ab8c with T5-large; about 6.87 GB with the text encoder.</summary>
    public static readonly LocalMusicVariant AudioMedium = new("audio-magnet-medium", "audio-magnet-medium", 10, [.. LocalMusicFiles.From("facebook/audio-magnet-medium", "d61ab8c298ab73d652d3da543f4ff0af173638c8", "audio-magnet-medium",
    [
        new("state_dict.bin", Sha256: "039fc4f79953764397c5389a025117c04c1f701ff1d7113926933d67376f8283", 3_678_456_979),
        new("compression_state_dict.bin", Sha256: "776320589e3cefc0938f1b3105f19b6b11faeef38478b48ea6f7ce48112189f1", 235_742_595),
    ]), .. T5Large], "t5-large");

    /// <summary>Every MAGNeT model, in the catalog's order.</summary>
    public static readonly IReadOnlyList<LocalMusicVariant> All = [Small10, Small30, Medium10, Medium30, AudioSmall, AudioMedium];
}
