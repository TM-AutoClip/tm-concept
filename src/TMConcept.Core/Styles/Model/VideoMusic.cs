namespace TMConcept.Core.Styles;

/// <summary>
/// A template's background music (0.1.44, C3; part of <see cref="VideoStyle"/>, stored in its JSON: a template saved before has none and
/// reads as <see cref="MusicSource.Off"/>, so its videos sound as they did). The levels are the defaults of <see cref="MusicMixDefaults"/>;
/// <see cref="Styles.VideoStyleRules"/> keeps them inside the editor's bounds when a video reads them.
/// </summary>
public sealed class VideoMusic
{
    /// <summary>Where the music comes from.</summary>
    public MusicSource Source { get; set; } = MusicSource.Off;

    /// <summary>
    /// The user's own file (<see cref="MusicSource.OwnFile"/>): its full path on this computer. The template keeps only the path; every video
    /// copies the file into its version folder when it starts, so a new version of that video uses the same music.
    /// </summary>
    public string File { get; set; } = string.Empty;

    /// <summary>
    /// The library track (0.1.45, <see cref="MusicSource.Library"/>): a track of the ready library, or one picked from an Openverse search (its
    /// <see cref="Music.MusicTrack.File"/> is then its download on this computer). Null = each video's concept chooses a library track from its
    /// scene plan's music hints.
    /// </summary>
    public Music.MusicTrack? Track { get; set; }

    /// <summary>The attribution the track asks for (optional; empty = none). Shown after the video for the description, never drawn into it.</summary>
    public string Attribution { get; set; } = string.Empty;

    /// <summary>The music's level where nobody speaks (dB).</summary>
    public double LevelDb { get; set; } = MusicMixDefaults.LevelDb;

    /// <summary>How much lower the music is under narration (dB, 0 or below).</summary>
    public double DuckDb { get; set; } = MusicMixDefaults.DuckDb;

    /// <summary>The fade-out at the end of the video (seconds).</summary>
    public double FadeOutSeconds { get; set; } = MusicMixDefaults.FadeOutSeconds;
}
