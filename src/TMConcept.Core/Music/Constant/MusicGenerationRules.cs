namespace TMConcept.Core.Music;

/// <summary>
/// The numbers of generated music (0.1.46, C5; plan §3 "Konsepte özel üretim"). A piece shorter than the video is looped by the bed planner
/// (F8, <see cref="MusicBedPlanner"/>): no second piece is made, so a long video costs no more than <see cref="MaxSeconds"/> of music.
/// </summary>
public static class MusicGenerationRules
{
    /// <summary>The shortest piece asked for (rakkase's plan accepts 10–600 s; ElevenLabs 3–600 s).</summary>
    public const int MinSeconds = 10;

    /// <summary>The longest piece asked for; a longer video loops it.</summary>
    public const int MaxSeconds = 120;

    /// <summary>A whole-song model's length when it is billed per piece and its length is only asked for in the prompt (Lyria 3.5).</summary>
    public const int SongSeconds = 120;

    /// <summary>The local model's install on disk (rakkase's measurement: about 23 GB, the weights about 6.4 GB of it).</summary>
    public const long LocalInstallBytes = 23_000_000_000;

    /// <summary>The local model's weights (22 files, their sizes added up from rakkase's pinned list).</summary>
    public const long LocalWeightBytes = 6_381_794_138;

    /// <summary>The most characters of a prompt (rakkase's caption limit is 256 tokens; the online models take this easily).</summary>
    public const int MaxPromptCharacters = 600;

    /// <summary>The most characters of a song's words (28 Nov 2026, the models that sing them: SongGeneration, YuE).</summary>
    public const int MaxLyricsCharacters = 3000;

    /// <summary>The seed of a piece when nothing else gives one.</summary>
    public const int DefaultSeed = 1987;

    /// <summary>The folder of a run's work folder the piece is made in.</summary>
    public const string WorkFolder = "music-generated";

    /// <summary>The work folder of the music page's pieces (under the app's work folder; removed after each piece).</summary>
    public const string MakeWorkFolder = "music-make";

    /// <summary>How often a running local music piece writes a line to the log (0.1.59: time so far and the script's last line).</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(60);

    /// <summary>The length a piece on the music page starts with (seconds).</summary>
    public const int DefaultSeconds = 60;

    /// <summary>The most words of a piece's name taken from its prompt.</summary>
    public const int TitleWords = 8;

    /// <summary>The most characters of a piece's name taken from its prompt.</summary>
    public const int TitleCharacters = 60;
}
