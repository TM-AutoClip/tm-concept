namespace TMConcept.Core.Subtitles;

/// <summary>
/// Hears a narration file and gives its words with their times (0.1.48, S3: whisper.cpp, a component installed only after the user's yes).
/// Only the subtitles' timing uses it; the text always stays the script's own. One file at a time, a low-priority process.
/// </summary>
public interface IWordRecognizer
{
    /// <summary>Whether this computer has a build of it at all (false: the subtitles keep their estimated times, with a yellow note).</summary>
    bool Supported { get; }

    /// <summary>Whether the program and its model are installed (files only; no process).</summary>
    bool Ready { get; }

    /// <summary>
    /// The words heard in <paramref name="audioFile"/> (<paramref name="language"/>: the narration's language code, "tr", "en"…), in order;
    /// throws <see cref="InvalidOperationException"/> when the program fails.
    /// </summary>
    Task<IReadOnlyList<RecognizedWord>> RecognizeAsync(string audioFile, string language, CancellationToken cancellationToken);
}
