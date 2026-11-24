using System.Globalization;

namespace TMConcept.Core.Plans;

/// <summary>
/// The common part of the plan rules: each scene's plan as answered (the first one of a scene number; a doubled or missing scene is the
/// structure check's finding), the base it will really get (as <see cref="PlanReader.Clean"/> decides it), its background and its points.
/// </summary>
public abstract class PlanRuleBase : IPlanRule
{
    /// <summary>Numbers in findings are written the same in every culture.</summary>
    protected static CultureInfo C => CultureInfo.InvariantCulture;

    /// <inheritdoc />
    public IEnumerable<string> Problems(IReadOnlyList<PlanItem> items, PlanCheck check)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(check);
        return Find(items, check).ToList();
    }

    /// <summary>The rule's own findings.</summary>
    protected abstract IEnumerable<string> Find(IReadOnlyList<PlanItem> items, PlanCheck check);

    /// <summary>The plan of every scene of the video that has one, in scene order (scene number, plan).</summary>
    protected static IEnumerable<(int Scene, RenderPlan Plan)> Scenes(IReadOnlyList<PlanItem> items, PlanCheck check) =>
        Enumerable.Range(1, check.Scenes.Count).Select(n => (n, items.FirstOrDefault(i => i.Scene == n)?.Plan)).Where(p => p.Item2 is not null)
            .Select(p => (p.n, p.Item2!));

    /// <summary>The base the scene will be drawn on: an unknown base, or an image base without a picture, becomes the picture or a title.</summary>
    protected static string BaseOf(RenderPlan plan, int scene, PlanCheck check)
    {
        var picture = scene <= check.Pictures.Count && check.Pictures[scene - 1];
        return !PlanBases.All.Contains(plan.Base) || (plan.Base == PlanBases.Image && !picture) ? (picture ? PlanBases.Image : PlanBases.Title) : plan.Base;
    }

    /// <summary>
    /// What the scene stands on, as the viewer sees it: each scene's own picture is its own background; maps of the same region (to 5°) are
    /// the same background, as is the same diagram (the same box labels) and every title card (a plain card).
    /// </summary>
    protected static string BackgroundOf(RenderPlan plan, int scene, PlanCheck check) => BaseOf(plan, scene, check) switch
    {
        PlanBases.Image => "image " + scene.ToString(C),
        PlanBases.Map => "map " + Region(plan),
        PlanBases.Diagram => "diagram " + string.Join("|", plan.Nodes.Select(n => n.Label.Trim().ToLowerInvariant()).Order(StringComparer.Ordinal)),
        _ => PlanBases.Title,
    };

    /// <summary>The ids an action can point at in its scene: the points that have a place (as cleaning keeps them) and the boxes.</summary>
    protected static HashSet<string> Points(RenderPlan plan) => plan.Anchors
        .Where(a => a.Id.Length > 0 && ((a.Lon is not null && a.Lat is not null) || (a.X is not null && a.Y is not null))).Select(a => a.Id)
        .Concat(plan.Nodes.Where(n => n.Id.Length > 0).Select(n => n.Id)).ToHashSet(StringComparer.Ordinal);

    /// <summary>A short text of the model's for a finding: one line, quotes that cannot close the data marks.</summary>
    protected static string Quote(string text) =>
        "«" + Text.TextClean.OneLine(text, 60).Replace("«", "\"", StringComparison.Ordinal).Replace("»", "\"", StringComparison.Ordinal) + "»";

    /// <summary>A map's region to 5° (the one cleaning would make around its places when it has none).</summary>
    private static string Region(RenderPlan plan)
    {
        var region = plan.Map?.Region ?? [];
        if (region.Count != 4 || region[0] >= region[2] || region[1] >= region[3])
        {
            var places = plan.Anchors.Where(a => a.Lon is not null && a.Lat is not null).ToList();
            region = places.Count == 0
                ? [-170, -58, 190, 80]
                : [places.Min(a => a.Lon!.Value), places.Min(a => a.Lat!.Value), places.Max(a => a.Lon!.Value), places.Max(a => a.Lat!.Value)];
        }

        return string.Join(",", region.Select(r => (Math.Round(r / 5) * 5).ToString(C)));
    }
}
