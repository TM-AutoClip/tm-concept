namespace TMConcept.Core.Subtitles;

/// <summary>
/// The subtitles of a video and how they were timed: <see cref="Sentences"/> is empty when the words' times were estimated (no recogniser),
/// <see cref="Warnings"/> holds the video warning codes of the timing (<see cref="Videos.VideoWarnings.Subtitles"/>).
/// </summary>
public sealed record SubtitleTimingResult(IReadOnlyList<SubtitleCue> Cues, IReadOnlyList<AlignedSentence> Sentences, IReadOnlyList<string> Warnings)
{
    /// <summary>Whether the words' times came from the voice (recognised or given by the voice engine).</summary>
    public bool Heard => Sentences.Count > 0;
}
