namespace TMConcept.Core.Styles;

/// <summary>
/// The music sources a template can choose now (0.1.44: off and the user's own file; 0.1.45: the ready library with the Openverse search;
/// generated music follows).
/// </summary>
public static class MusicSources
{
    /// <summary>The sources shown in the template editor, in their order (the library only usable once it is installed).</summary>
    public static IReadOnlyList<MusicSource> Available { get; } = [MusicSource.Off, MusicSource.Library, MusicSource.OwnFile];

    /// <summary>The audio file kinds the user can choose as own music (ffmpeg reads them all).</summary>
    public static IReadOnlyList<string> FilePatterns { get; } = ["*.mp3", "*.wav", "*.ogg", "*.opus", "*.m4a", "*.aac", "*.flac"];
}
