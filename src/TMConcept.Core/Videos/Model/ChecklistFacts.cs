namespace TMConcept.Core.Videos;

/// <summary>What the video page knows before production (F16, 0.1.49), read off the window's thread; <see cref="ProductionChecklist"/> decides from it.</summary>
public sealed class ChecklistFacts
{
    /// <summary>A template is chosen.</summary>
    public bool Template { get; init; }

    /// <summary>A video code model is chosen.</summary>
    public bool CodeModel { get; init; }

    /// <summary>The providers of the chosen online models that have no API key (no request is made to find them).</summary>
    public IReadOnlyList<string> ProvidersWithoutKey { get; init; } = [];

    /// <summary>The narration step has a voice.</summary>
    public bool Voice { get; init; }

    /// <summary>The template's music is the user's own file.</summary>
    public bool OwnMusic { get; init; }

    /// <summary>That file is on this computer.</summary>
    public bool MusicExists { get; init; }

    /// <summary>ffmpeg reads it as sound (null = not checked: ffmpeg is not installed yet).</summary>
    public bool? MusicReadable { get; init; }

    /// <summary>The user's pronunciation dictionary of the video's language has no word.</summary>
    public bool PronunciationEmpty { get; init; }
}
