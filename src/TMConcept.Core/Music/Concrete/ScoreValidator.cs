using System.Globalization;

namespace TMConcept.Core.Music;

/// <summary>
/// Checks a score before it is written and played (0.1.55, "Program composes"; pure): the tempo, the grid and the length within
/// <see cref="ScoreRules"/>, at most one track per MIDI channel, every instrument, volume, place, note and velocity a MIDI value, every note
/// inside the piece. The app's own composer always passes; a writer that takes a score from elsewhere (a text model) is held to the same rules.
/// </summary>
public static class ScoreValidator
{
    /// <summary>What is wrong with <paramref name="score"/> (empty = nothing).</summary>
    public static IReadOnlyList<string> Problems(MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        var problems = new List<string>();
        if (score.Bpm is < ScoreRules.MinBpm or > ScoreRules.MaxBpm)
        {
            problems.Add(Text($"tempo {score.Bpm} is outside {ScoreRules.MinBpm}-{ScoreRules.MaxBpm}"));
        }

        if (score.TicksPerQuarter != ScoreRules.TicksPerQuarter || score.BeatsPerBar != ScoreRules.BeatsPerBar)
        {
            problems.Add(Text($"the grid must be {ScoreRules.TicksPerQuarter} ticks a quarter, {ScoreRules.BeatsPerBar} beats a bar"));
        }

        if (score.Bars <= 0 || (score.Bpm > 0 && score.Seconds > ScoreRules.MaxSeconds))
        {
            problems.Add(Text($"{score.Bars} bars do not make a piece of 1-{ScoreRules.MaxSeconds} s"));
        }

        if (score.Tracks.Count is 0 or > ScoreRules.MaxTracks)
        {
            problems.Add(Text($"{score.Tracks.Count} tracks (1-{ScoreRules.MaxTracks})"));
        }

        if (score.Tracks.GroupBy(t => t.Channel).FirstOrDefault(g => g.Count() > 1) is { } shared)
        {
            problems.Add(Text($"channel {shared.Key} has more than one track"));
        }

        if (score.Tracks.Sum(t => t.Notes.Count) > ScoreRules.MaxNotes)
        {
            problems.Add(Text($"more than {ScoreRules.MaxNotes} notes"));
        }

        foreach (var track in score.Tracks)
        {
            problems.AddRange(TrackProblems(track, score.EndTick));
        }

        return problems;
    }

    private static IEnumerable<string> TrackProblems(ScoreTrack track, int end)
    {
        if (track.Channel is < 0 or > ScoreRules.MaxChannel)
        {
            yield return Text($"{track.Name}: channel {track.Channel}");
        }

        if (!Data(track.Program) || !Data(track.Volume) || !Data(track.Pan))
        {
            yield return Text($"{track.Name}: instrument, volume or place is not a MIDI value");
        }

        if (track.Notes.FirstOrDefault(n => n.Start < 0 || n.Length <= 0 || n.Start + n.Length > end) is { } outside)
        {
            yield return Text($"{track.Name}: a note at tick {outside.Start} is outside the piece");
        }

        if (track.Notes.FirstOrDefault(n => !Data(n.Pitch) || n.Velocity is < 1 or > ScoreRules.MaxData) is { } wrong)
        {
            yield return Text($"{track.Name}: note {wrong.Pitch} with velocity {wrong.Velocity} is not a MIDI note");
        }
    }

    private static bool Data(int value) => value is >= 0 and <= ScoreRules.MaxData;

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
