namespace TMConcept.Core.Pronunciation;

/// <summary>
/// A word of the user's pronunciation dictionary: in videos of <see cref="Language"/>, the voice is given <see cref="Spoken"/> where the
/// narration has <see cref="Word"/> (a whole word; upper and lower case as the language writes them). The subtitles keep <see cref="Word"/>.
/// </summary>
public sealed class PronunciationEntry
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Output language code ("tr").</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>The word (or words) as written ("Viking", "New York").</summary>
    public string Word { get; set; } = string.Empty;

    /// <summary>How the voice should read it, written the way the language reads letters ("Vikink").</summary>
    public string Spoken { get; set; } = string.Empty;

    /// <summary>Added (UTC).</summary>
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
