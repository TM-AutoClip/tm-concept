namespace TMConcept.Core.Styles;

/// <summary>The music sources a template can choose now (0.1.44: off and the user's own file; the library and generated music follow).</summary>
public static class MusicSources
{
    /// <summary>The sources shown in the template editor, in their order.</summary>
    public static IReadOnlyList<MusicSource> Available { get; } = [MusicSource.Off, MusicSource.OwnFile];

    /// <summary>The audio file kinds the user can choose as own music (ffmpeg reads them all).</summary>
    public static IReadOnlyList<string> FilePatterns { get; } = ["*.mp3", "*.wav", "*.ogg", "*.opus", "*.m4a", "*.aac", "*.flac"];
}
