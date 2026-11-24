namespace TMConcept.Core.Subtitles;

/// <summary>
/// One sentence of the narration after alignment: its words with times, and the share of its letters the recogniser heard
/// (<see cref="MatchRatio"/>; below <see cref="CaptionTiming.MatchThreshold"/> the sentence is listed with "check by ear").
/// </summary>
public sealed record AlignedSentence(string Text, IReadOnlyList<TimedWord> Words, double MatchRatio)
{
    /// <summary>Too little of it was heard: listen to it before publishing.</summary>
    public bool NeedsListening => MatchRatio < CaptionTiming.MatchThreshold;
}
