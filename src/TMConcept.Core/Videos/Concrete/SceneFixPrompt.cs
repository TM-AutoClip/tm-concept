using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TMConcept.Core.Chat;
using TMConcept.Core.Render;

namespace TMConcept.Core.Videos;

/// <summary>
/// The one call of "Fix" (F16, 0.1.49): the scenes the user left notes on, with their narration (placeholders included), what they show
/// and, for a video drawn by the app, their scene plan; every note in one request. The answer names, per noted scene, its new narration
/// (empty = keep), a new image prompt (empty = keep the picture) and, for a drawn video, its new plan in the plan's own format
/// (<see cref="Plans.PlanPromptBuilder.Schema"/>). Scenes without a note are not sent and never changed.
/// </summary>
public static class SceneFixPrompt
{
    /// <summary>The system part.</summary>
    public static string System(bool plans) =>
        "You fix scenes of a finished explainer video after the viewer's notes. For each scene you get, return: \"narration\" — the scene's "
        + "new narration in the video's language when a note asks to change what is said, otherwise an empty string (keep the bracketed "
        + "placeholders such as pauses where they still fit); \"image\" — a new English image prompt for the scene's picture when a note is about "
        + "what the picture shows, otherwise an empty string. "
        + (plans
            ? "Also return the scene's whole plan in the same format as before (base, title, region, nodes, links, anchors, beats); change what the "
                + "notes ask for, keep the rest. One beat per narration sentence at most. "
            : string.Empty)
        + "Return only the scenes you were given, by their numbers.";

    /// <summary>The user part: the noted scenes with their texts, plans and notes.</summary>
    public static string User(RenderInput input, IReadOnlyList<string> texts, IReadOnlyList<SceneNote> notes, bool plans)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(notes);
        var c = CultureInfo.InvariantCulture;
        var text = new StringBuilder().Append(c, $"Video «{Text.TextClean.OneLine(input.Title, 200)}», language {input.Language}.\n");
        foreach (var note in notes.OrderBy(n => n.Scene))
        {
            if (note.Scene < 1 || note.Scene > input.Scenes.Count)
            {
                continue;
            }

            var scene = input.Scenes[note.Scene - 1];
            text.Append(c, $"\nScene {note.Scene} ({scene.DurationSeconds:0.#} s)\n");
            text.Append(c, $"Narration: «{Text.TextClean.OneLine(texts.ElementAtOrDefault(note.Scene - 1) ?? scene.Caption, 4000)}»\n");
            if (scene.Visual.Length > 0)
            {
                text.Append(c, $"On screen: «{Text.TextClean.OneLine(scene.Visual, 600)}»\n");
            }

            text.Append(scene.Image.Length > 0 ? "It shows a generated picture.\n" : "It has no generated picture.\n");
            if (plans && scene.Plan is { } plan)
            {
                text.Append("Plan: ").Append(JsonSerializer.Serialize(plan, CoreJsonContext.Default.RenderPlan)).Append('\n');
            }

            text.Append(c, $"Viewer's note: «{Text.TextClean.OneLine(note.Text, 1200)}»\n");
        }

        return text.Append(c, $"\nReturn {notes.Count(n => n.Scene >= 1 && n.Scene <= input.Scenes.Count)} scenes.").ToString();
    }

    /// <summary>The answer format: the plan's scene format with the narration and the image prompt added, or only those three fields.</summary>
    public static JsonObject Schema(bool plans)
    {
        if (plans)
        {
            var schema = Plans.PlanPromptBuilder.Schema();
            var scene = (JsonObject)schema["properties"]!["scenes"]!["items"]!;
            ((JsonObject)scene["properties"]!)["narration"] = new JsonObject { ["type"] = "string" };
            ((JsonObject)scene["properties"]!)["image"] = new JsonObject { ["type"] = "string" };
            ((JsonArray)scene["required"]!).Add((JsonNode)JsonValue.Create("narration"));
            ((JsonArray)scene["required"]!).Add((JsonNode)JsonValue.Create("image"));
            return schema;
        }

        var item = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["scene"] = new JsonObject { ["type"] = "integer" },
                ["narration"] = new JsonObject { ["type"] = "string" },
                ["image"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("scene", "narration", "image"),
            ["additionalProperties"] = false,
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["scenes"] = new JsonObject { ["type"] = "array", ["items"] = item } },
            ["required"] = new JsonArray("scenes"),
            ["additionalProperties"] = false,
        };
    }

    /// <summary>The narration and image prompt the answer gives each scene (trimmed; empty = keep), by scene number.</summary>
    public static IReadOnlyDictionary<int, (string Narration, string Image)> Read(string? answer)
    {
        var result = new Dictionary<int, (string, string)>();
        if (ModelJson.TryParseObject(answer)?["scenes"] is not JsonArray scenes)
        {
            return result;
        }

        foreach (var scene in scenes.OfType<JsonObject>())
        {
            if (scene["scene"] is JsonValue number && number.TryGetValue(out int n) && n > 0)
            {
                result[n] = (Value(scene["narration"]), Value(scene["image"]));
            }
        }

        return result;
    }

    private static string Value(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text.Trim() : string.Empty;
}
