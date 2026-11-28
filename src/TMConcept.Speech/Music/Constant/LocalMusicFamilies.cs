namespace TMConcept.Speech.Music;

/// <summary>
/// The local music families besides ACE-Step 1.5 (28 Nov 2026; ACE-Step runs through rakkase, <see cref="RakkaseMusicGenerator"/>): MusicGen,
/// MAGNeT, AudioLDM 2, DiffRhythm (1.2 and 1.0, sharing their environment and their shared files), SongGeneration, YuE2 and YuE 1. The ones
/// that sing take the user's words (<see cref="LocalMusicFamily.Lyrics"/>).
/// </summary>
public static class LocalMusicFamilies
{
    /// <summary>MusicGen (its script: <c>music_musicgen.py</c>).</summary>
    public static readonly LocalMusicFamily MusicGen = new("musicgen", "MusicGen", LocalMusicEnvironments.MusicGen, "music_musicgen.py", MusicGenModels.All);

    /// <summary>MAGNeT (its script: <c>music_magnet.py</c>, which runs audiocraft from its pinned source).</summary>
    public static readonly LocalMusicFamily Magnet = new("magnet", "MAGNeT", LocalMusicEnvironments.Magnet, "music_magnet.py", MagnetModels.All,
        AudiocraftPins.Archive);

    /// <summary>AudioLDM 2 (its script: <c>music_audioldm2.py</c>).</summary>
    public static readonly LocalMusicFamily AudioLdm2 = new("audioldm2", "AudioLDM 2", LocalMusicEnvironments.AudioLdm2, "music_audioldm2.py", AudioLdm2Models.All);

    /// <summary>DiffRhythm 1.2 (its script: <c>music_diffrhythm.py</c>, with DiffRhythm's newest code).</summary>
    public static readonly LocalMusicFamily DiffRhythm = new("diffrhythm", "DiffRhythm", LocalMusicEnvironments.DiffRhythm, "music_diffrhythm.py",
        DiffRhythmModels.Current, DiffRhythmPins.Current, "Apache-2.0", "diffrhythm");

    /// <summary>DiffRhythm 1.0 (the same script, with DiffRhythm's code before 1.2; its files in DiffRhythm's folder).</summary>
    public static readonly LocalMusicFamily DiffRhythmLegacy = new("diffrhythm-1.0", "DiffRhythm 1.0", LocalMusicEnvironments.DiffRhythm, "music_diffrhythm.py",
        DiffRhythmModels.Legacy, DiffRhythmPins.Legacy, "Apache-2.0", "diffrhythm-1.0", Folder: "diffrhythm");

    /// <summary>SongGeneration (its script: <c>music_songgeneration.py</c>; sings the user's words).</summary>
    public static readonly LocalMusicFamily SongGeneration = new("songgeneration", "SongGeneration", LocalMusicEnvironments.SongGeneration,
        "music_songgeneration.py", SongGenerationModels.All, SongGenerationPins.Archive, "License Terms of SongGeneration, non-commercial", "songgeneration",
        Lyrics: true);

    /// <summary>YuE2 (its script: <c>music_yue2.py</c>; sings the user's words).</summary>
    public static readonly LocalMusicFamily Yue2 = new("yue2", "YuE2", LocalMusicEnvironments.Yue2, "music_yue2.py", YueModels.Yue2Models, YuePins.Yue2,
        "Apache-2.0", "yue2", Lyrics: true);

    /// <summary>YuE 1 (its script: <c>music_yue.py</c>, through YuE-exllamav2; sings the user's words).</summary>
    public static readonly LocalMusicFamily Yue = new("yue", "YuE", LocalMusicEnvironments.Yue, "music_yue.py", YueModels.Yue1Models, YuePins.Exl2,
        "Apache-2.0", "yue-exllamav2", Lyrics: true);

    /// <summary>Every family.</summary>
    public static readonly IReadOnlyList<LocalMusicFamily> All = [MusicGen, Magnet, AudioLdm2, DiffRhythm, DiffRhythmLegacy, SongGeneration, Yue2, Yue];
}
