using TMConcept.Core.Subtitles;

namespace TMConcept.Tests.Core;

/// <summary>A recogniser that "hears" the words it was given (no process): whisper.cpp's place in the subtitle timing tests.</summary>
public sealed class FakeWordRecognizer(bool supported, bool ready, Func<string, IReadOnlyList<RecognizedWord>>? hear = null) : IWordRecognizer
{
    /// <summary>The files it was asked to hear.</summary>
    public List<string> Heard { get; } = [];

    /// <inheritdoc />
    public bool Supported => supported;

    /// <inheritdoc />
    public bool Ready => ready;

    /// <inheritdoc />
    public Task<IReadOnlyList<RecognizedWord>> RecognizeAsync(string audioFile, string language, CancellationToken cancellationToken)
    {
        Heard.Add(audioFile);
        return Task.FromResult(hear is null ? [] : hear(audioFile));
    }
}
