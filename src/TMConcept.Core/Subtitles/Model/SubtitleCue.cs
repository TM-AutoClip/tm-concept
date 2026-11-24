namespace TMConcept.Core.Subtitles;

/// <summary>
/// One subtitle: time range and at most two lines. <see cref="Words"/> (0.1.48): the real time of each word of the lines, in order, when the
/// words were heard (whisper.cpp or a voice engine that gives times); empty when their times are estimated.
/// </summary>
public sealed record SubtitleCue(long StartMs, long EndMs, IReadOnlyList<string> Lines)
{
    /// <summary>The words of <see cref="Lines"/> with their real times (empty: estimated by length).</summary>
    public IReadOnlyList<TimedWord> Words { get; init; } = [];
}
