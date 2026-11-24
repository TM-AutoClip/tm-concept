namespace TMConcept.Core.Plans;

/// <summary>
/// One background fills at most 40 % of the video (<see cref="PlanLimits.BackgroundShare"/>; a plan that keeps the same map, diagram or
/// title card gets dull). Only a background of two or more scenes counts (one long scene cannot be helped), from three scenes on.
/// </summary>
public sealed class BackgroundShareRule : PlanRuleBase
{
    /// <inheritdoc />
    protected override IEnumerable<string> Find(IReadOnlyList<PlanItem> items, PlanCheck check)
    {
        var total = check.Scenes.Sum(s => Math.Max(0, s.Seconds));
        if (check.Scenes.Count < PlanLimits.BackgroundMinScenes || total <= 0)
        {
            yield break;
        }

        foreach (var group in Scenes(items, check).GroupBy(p => BackgroundOf(p.Plan, p.Scene, check), StringComparer.Ordinal))
        {
            var scenes = group.Select(p => p.Scene).ToList();
            var share = scenes.Sum(n => Math.Max(0, check.Scenes[n - 1].Seconds)) / total;
            if (scenes.Count >= 2 && share > PlanLimits.BackgroundShare)
            {
                var what = group.Key.Split(' ')[0] switch
                {
                    PlanBases.Map => "the same map region",
                    PlanBases.Diagram => "the same diagram",
                    _ => "title cards",
                };
                yield return string.Create(C,
                    $"one background ({what}) fills {share * 100:0}% of the video (scenes {string.Join(", ", scenes)}; at most {PlanLimits.BackgroundShare * 100:0}%): give some of them another base, region or picture");
            }
        }
    }
}
