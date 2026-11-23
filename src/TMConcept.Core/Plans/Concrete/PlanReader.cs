using System.Globalization;
using System.Text.Json.Nodes;
using TMConcept.Core.Chat;

namespace TMConcept.Core.Plans;

/// <summary>
/// Reads and checks the plan answer (0.1.33). <see cref="Read"/> is tolerant (thinking, a code fence and missing fields are skipped);
/// <see cref="Problems"/> names what makes the plan unusable (the scene count, a missing or doubled scene), which gets one repair request;
/// <see cref="Clean"/> then repairs what can be repaired without asking: an unknown base (or an image base without a picture) becomes the
/// picture or a title, unknown actions and actions pointing at points that do not exist are dropped, zooms, sentences and fractions are
/// clamped, a map without a region gets one around its places. Nothing a model writes reaches the page unchecked.
/// </summary>
public static class PlanReader
{
    /// <summary>The scenes of an answer in its order (fewer than asked, or none).</summary>
    public static IReadOnlyList<PlanItem> Read(string? answer)
    {
        if (ModelJson.TryParseObject(answer)?["scenes"] is not JsonArray scenes)
        {
            return [];
        }

        return scenes.OfType<JsonObject>().Select(ReadScene).ToList();
    }

    /// <summary>What does not fit <paramref name="count"/> scenes (empty = usable); in English, for the log and the repair request.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<PlanItem> items, int count)
    {
        ArgumentNullException.ThrowIfNull(items);
        var c = CultureInfo.InvariantCulture;
        var problems = new List<string>();
        if (items.Count != count)
        {
            problems.Add(string.Create(c, $"{items.Count} scene plans for {count} scenes"));
        }

        for (var scene = 1; scene <= count; scene++)
        {
            var mine = items.Count(i => i.Scene == scene);
            if (mine != 1)
            {
                problems.Add(mine == 0 ? string.Create(c, $"scene {scene} has no plan") : string.Create(c, $"scene {scene} has {mine} plans"));
            }
        }

        return problems;
    }

    /// <summary>
    /// The plans in scene order, each cleaned (<paramref name="sentences"/>: the number of narration sentences of every scene;
    /// <paramref name="pictures"/>: whether each scene has a generated image). A scene the answer left out gets its picture, or a title,
    /// without beats.
    /// </summary>
    public static IReadOnlyList<PlanItem> Clean(IReadOnlyList<PlanItem> items, IReadOnlyList<int> sentences, IReadOnlyList<bool> pictures)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sentences);
        ArgumentNullException.ThrowIfNull(pictures);
        var result = new List<PlanItem>();
        for (var scene = 1; scene <= sentences.Count; scene++)
        {
            var picture = scene <= pictures.Count && pictures[scene - 1];
            var item = items.FirstOrDefault(i => i.Scene == scene)
                ?? new PlanItem { Scene = scene, Plan = new RenderPlan { Base = picture ? PlanBases.Image : PlanBases.Title } };
            var plan = item.Plan;
            if (!PlanBases.All.Contains(plan.Base) || (plan.Base == PlanBases.Image && !picture))
            {
                plan.Base = picture ? PlanBases.Image : PlanBases.Title;
            }

            var count = Math.Max(1, sentences[scene - 1]);
            CleanPoints(plan);
            if (plan.Base == PlanBases.Map)
            {
                CleanMap(plan);
            }
            else
            {
                plan.Map = null;
            }

            var ids = plan.Anchors.Select(a => a.Id).Concat(plan.Nodes.Select(n => n.Id)).ToHashSet(StringComparer.Ordinal);
            plan.Links = plan.Links.Where(l => ids.Contains(l.From) && ids.Contains(l.To)).ToList();
            foreach (var beat in plan.Beats)
            {
                beat.Sentence = Math.Clamp(beat.Sentence, 1, count);
                if (beat.Camera is { } camera)
                {
                    camera.Zoom = Math.Clamp(double.IsFinite(camera.Zoom) ? camera.Zoom : 1, 1, 2.2);
                    if (camera.Focus.Length > 0 && !ids.Contains(camera.Focus))
                    {
                        camera.Focus = string.Empty;
                    }
                }

                beat.Actions = beat.Actions.Where(a => Usable(a, plan, ids)).Take(4).ToList();
                foreach (var action in beat.Actions)
                {
                    action.Via = action.Via.Where(ids.Contains).ToList();
                    action.Walker = PlanActions.Walkers.Contains(action.Walker) ? action.Walker : string.Empty;
                    action.Label = Text.TextClean.OneLine(action.Label, 40);
                    action.Text = Text.TextClean.OneLine(action.Text, 80);
                    action.Suffix = Text.TextClean.OneLine(action.Suffix, 8);
                    action.Size = Math.Clamp(action.Size, 0, 0.3);
                }
            }

            // One beat per sentence: a second beat of the same sentence joins the first.
            plan.Beats = plan.Beats.GroupBy(b => b.Sentence).OrderBy(g => g.Key).Select(g =>
            {
                var first = g.First();
                first.Camera ??= g.Select(b => b.Camera).FirstOrDefault(cam => cam is not null);
                first.Actions = g.SelectMany(b => b.Actions).Take(4).ToList();
                return first;
            }).ToList();
            plan.Title = Text.TextClean.OneLine(plan.Title, 120);
            item.Scene = scene;
            result.Add(item);
        }

        return result;
    }

    /// <summary>Whether an action can be drawn: a known kind whose points exist in its scene.</summary>
    private static bool Usable(PlanAction action, RenderPlan plan, HashSet<string> ids) => action.Type switch
    {
        PlanActions.Path or PlanActions.Arrow => ids.Contains(action.From) && ids.Contains(action.To),
        PlanActions.Pin or PlanActions.Label or PlanActions.Highlight => ids.Contains(action.At),
        PlanActions.Counter => action.Value is not null && (action.At.Length == 0 || ids.Contains(action.At)),
        PlanActions.Country => plan.Base == PlanBases.Map && action.Country.Length == 3,
        PlanActions.Text => action.Text.Length > 0,
        _ => false,
    };

    /// <summary>Drops points without coordinates, clamps fractions into the frame above the subtitles and trims labels.</summary>
    private static void CleanPoints(RenderPlan plan)
    {
        plan.Anchors = plan.Anchors.Where(a => a.Id.Length > 0 && ((a.Lon is not null && a.Lat is not null) || (a.X is not null && a.Y is not null)))
            .GroupBy(a => a.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        foreach (var anchor in plan.Anchors)
        {
            anchor.Label = Text.TextClean.OneLine(anchor.Label, 40);
            anchor.Find = Text.TextClean.OneLine(anchor.Find, 120);
            if (anchor.X is { } x && anchor.Y is { } y)
            {
                anchor.X = Math.Clamp(x, 0.04, 0.96);
                anchor.Y = Math.Clamp(y, 0.04, 0.78);
            }

            if (anchor.Lon is { } lon && anchor.Lat is { } lat)
            {
                anchor.Lon = Math.Clamp(lon, -180, 180);
                anchor.Lat = Math.Clamp(lat, -85, 85);
            }
        }

        plan.Nodes = plan.Nodes.Where(n => n.Id.Length > 0).GroupBy(n => n.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        foreach (var node in plan.Nodes)
        {
            node.Label = Text.TextClean.OneLine(node.Label, 30);
            node.X = Math.Clamp(node.X, 0.1, 0.9);
            node.Y = Math.Clamp(node.Y, 0.1, 0.75);
        }
    }

    /// <summary>A map without a usable region gets one around its places (with a margin); a region is kept within the world.</summary>
    private static void CleanMap(RenderPlan plan)
    {
        var region = plan.Map?.Region ?? [];
        var places = plan.Anchors.Where(a => a.Lon is not null && a.Lat is not null).ToList();
        if (region.Count != 4 || region[0] >= region[2] || region[1] >= region[3])
        {
            if (places.Count == 0)
            {
                region = [-170, -58, 190, 80];
            }
            else
            {
                var west = places.Min(a => a.Lon!.Value);
                var east = places.Max(a => a.Lon!.Value);
                var south = places.Min(a => a.Lat!.Value);
                var north = places.Max(a => a.Lat!.Value);
                var margin = Math.Max(4, Math.Max(east - west, north - south) * 0.25);
                region = [west - margin, south - margin, east + margin, north + margin];
            }
        }

        plan.Map = new PlanMap
        {
            Region = [Math.Clamp(region[0], -180, 179), Math.Clamp(region[1], -85, 84), Math.Clamp(region[2], -179, 180), Math.Clamp(region[3], -84, 85)],
        };
    }

    private static PlanItem ReadScene(JsonObject scene)
    {
        var plan = new RenderPlan
        {
            Base = Str(scene["base"]).ToLowerInvariant(),
            Title = Str(scene["title"]),
            Map = scene["region"] is JsonArray region ? new PlanMap { Region = region.Select(Num).Where(v => v is not null).Select(v => v!.Value).ToList() } : null,
            Nodes = Objects(scene["nodes"]).Select(n => new PlanNode { Id = Str(n["id"]), Label = Str(n["label"]), X = Num(n["x"]) ?? 0.5, Y = Num(n["y"]) ?? 0.5 }).ToList(),
            Links = Objects(scene["links"]).Select(l => new PlanLink { From = Str(l["from"]), To = Str(l["to"]) }).ToList(),
            Anchors = Objects(scene["anchors"]).Select(a => new PlanAnchor
            {
                Id = Str(a["id"]), Label = Str(a["label"]), Find = Str(a["find"]), Lon = Num(a["lon"]), Lat = Num(a["lat"]), X = Num(a["x"]), Y = Num(a["y"]),
            }).ToList(),
            Beats = Objects(scene["beats"]).Select(b => new PlanBeat
            {
                Sentence = (int)(Num(b["sentence"]) ?? 1),
                Camera = Str(b["focus"]).Length > 0 || (Num(b["zoom"]) ?? 1) > 1 ? new PlanCamera { Focus = Str(b["focus"]), Zoom = Num(b["zoom"]) ?? 1 } : null,
                Actions = Objects(b["actions"]).Select(a => new PlanAction
                {
                    Type = Str(a["type"]).ToLowerInvariant(), From = Str(a["from"]), To = Str(a["to"]),
                    Via = a["via"] is JsonArray via ? via.Select(Str).Where(v => v.Length > 0).ToList() : [],
                    At = Str(a["at"]), Label = Str(a["label"]), Text = Str(a["text"]), Walker = Str(a["walker"]).ToLowerInvariant(),
                    Dashed = a["dashed"] is JsonValue dashed && dashed.TryGetValue<bool>(out var d) && d, Country = Str(a["country"]).ToUpperInvariant(),
                    Size = Num(a["size"]) ?? 0, FromValue = Num(a["fromValue"]), Value = Num(a["value"]), Suffix = Str(a["suffix"]),
                }).ToList(),
            }).ToList(),
        };
        return new PlanItem { Scene = (int)(Num(scene["scene"]) ?? 0), Plan = plan };
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? node) => node is JsonArray array ? array.OfType<JsonObject>() : [];

    private static string Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : string.Empty;

    private static double? Num(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var number) && double.IsFinite(number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number)
            ? number
            : null;
    }
}
