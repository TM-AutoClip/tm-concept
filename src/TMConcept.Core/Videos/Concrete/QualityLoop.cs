namespace TMConcept.Core.Videos;

/// <summary>
/// The review rounds of the quality modes (F11, 0.1.50; ours): review → (careful mode) repair the scenes of the findings that count → review
/// again, at most <see cref="QualityLimits.MaxRounds"/> reviews. After <see cref="QualityLimits.PolishRounds"/> rounds only blockers count. It
/// stops when nothing counts, when there is no repair (standard mode: a report only), when the list did not get shorter after a repair (both
/// counted the same way, so dropping polish is not mistaken for progress), at the limit (no repair is made that would not be reviewed), or when
/// a repair changed nothing. A review that cannot run stops it too. No model is called here: the review and the repair are given.
/// </summary>
public static class QualityLoop
{
    /// <summary>
    /// Runs the rounds. <paramref name="review"/> gets the round's number and returns every finding (null: the review could not run);
    /// <paramref name="repair"/> (null: a report only) gets the counted findings and the round, and returns the scenes it repaired.
    /// </summary>
    public static async Task<(List<QualityRound> Rounds, string Stop)> RunAsync(Func<int, CancellationToken, Task<IReadOnlyList<QualityFinding>?>> review,
        Func<IReadOnlyList<QualityFinding>, int, CancellationToken, Task<IReadOnlyList<int>>>? repair, CancellationToken cancellationToken,
        int maxRounds = QualityLimits.MaxRounds)
    {
        ArgumentNullException.ThrowIfNull(review);
        var rounds = new List<QualityRound>();
        IReadOnlyList<QualityFinding>? previous = null;
        var limit = Math.Max(1, maxRounds);
        for (var round = 1; round <= limit; round++)
        {
            var found = await review(round, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return (rounds, QualityStops.Skipped);
            }

            var counted = QualityRules.Counted(found, round);
            var row = new QualityRound { Round = round, Findings = [.. counted] };
            rounds.Add(row);
            if (counted.Count == 0)
            {
                return (rounds, QualityStops.Clean);
            }

            if (repair is null)
            {
                return (rounds, QualityStops.ReportOnly);
            }

            if (previous is not null && counted.Count >= QualityRules.Counted(previous, round).Count)
            {
                return (rounds, QualityStops.Stuck);
            }

            if (round >= limit)
            {
                break;
            }

            var repaired = await repair(counted, round, cancellationToken).ConfigureAwait(false);
            row.Repaired = [.. repaired];
            if (repaired.Count == 0)
            {
                return (rounds, QualityStops.NoRepair);
            }

            previous = found;
        }

        return (rounds, QualityStops.Limit);
    }
}
