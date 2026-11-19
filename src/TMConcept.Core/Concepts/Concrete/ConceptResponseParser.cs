using System.Text.Json;

namespace TMConcept.Core.Concepts;

/// <summary>
/// Reads <c>{"konseptler":[{"baslik":"…","ozet":"…","icerik":"…"}]}</c> from a model answer. The answer is untrusted: a code fence or text
/// around the JSON is skipped, NUL and control characters are removed, lengths are cut. A cut-off answer (the model stopped in the middle)
/// still yields every complete item before the cut; a broken item is reported and skipped, the others are kept. English key names
/// (<c>concepts</c>, <c>title</c>, <c>summary</c>, <c>content</c>) are accepted too. Thinking blocks are skipped and an item that is not valid JSON is read
/// once more after <see cref="Chat.ModelJson.Repair"/> (0.1.17: small local models write raw line breaks inside the content).
/// 20 Nov 2026: a generated concept comes with its scenes as a list (<c>"sahneler":[{"ekranda":"…","seslendirme":"…"}]</c>, English
/// <c>scenes</c> / <c>on_screen</c> / <c>narration</c>); the program writes the stored text itself, one <c>[ekranda: …]</c> line (the scene
/// label of the prompt's language, <see cref="SceneLabel"/>) before each scene's narration, so <see cref="TMConcept.Core.Scenes.SceneSplitter"/>, the editor
/// and the storage stay as they were. An item without the list (an AI edit) still gives its <c>icerik</c>. Square brackets inside a scene's
/// text become round ones: only the program writes scene lines.
/// </summary>
public static class ConceptResponseParser
{
    private static readonly string[] ListKeys = ["konseptler", "concepts"];
    private static readonly string[] TitleKeys = ["baslik", "title"];
    private static readonly string[] SummaryKeys = ["ozet", "summary"];
    private static readonly string[] ContentKeys = ["icerik", "content"];
    private static readonly string[] SourceKeys = ["kaynaklar", "sources"];
    private static readonly string[] SceneKeys = ["sahneler", "scenes"];
    private static readonly string[] VisualKeys = ["ekranda", "on_screen", "onScreen", "visual"];
    private static readonly string[] NarrationKeys = ["seslendirme", "narration"];

    /// <summary>The label of the scene lines the program writes for a video in <paramref name="language"/> (the prompt's language).</summary>
    public static string SceneLabel(string language) =>
        Profiles.OutputLanguages.Of(language).PromptLanguage == Profiles.ContentLanguages.Turkish ? "ekranda" : "on screen";

    /// <summary>Parses with the English scene label.</summary>
    public static ConceptParseResult Parse(string? answer) => Parse(answer, "on screen");

    /// <summary>Parses; scene lists become text with <paramref name="sceneLabel"/> lines.</summary>
    public static ConceptParseResult Parse(string? answer, string sceneLabel)
    {
        var problems = new List<string>();
        var text = Chat.ModelJson.StripThinking(answer).Replace("\0", string.Empty, StringComparison.Ordinal);
        var start = ArrayStart(text);
        if (start < 0)
        {
            return new ConceptParseResult([], ["The answer has no concept list."], false);
        }

        var concepts = new List<ParsedConcept>();
        var (objects, complete) = TopLevelObjects(text, start);
        var number = 0;
        foreach (var json in objects)
        {
            number++;
            try
            {
                using var document = Document(json);
                var root = document.RootElement;
                var title = TextClean(Read(root, TitleKeys), ConceptLimits.TitleHardMax, oneLine: true);
                var summary = TextClean(Read(root, SummaryKeys), ConceptLimits.SummaryMax, oneLine: true);
                var content = SceneText(root, sceneLabel) ?? TextClean(Read(root, ContentKeys), ConceptLimits.ContentMax, oneLine: false);
                if (title.Length == 0 || content.Length == 0)
                {
                    problems.Add($"Item {number}: the title or the content is empty.");
                    continue;
                }

                concepts.Add(new ParsedConcept(title, summary, content) { Sources = Numbers(root, SourceKeys) });
            }
            catch (JsonException ex)
            {
                problems.Add($"Item {number}: not valid JSON ({ex.Message}).");
            }
        }

        if (!complete)
        {
            problems.Add("The answer was cut off; the complete items before the cut were kept.");
        }

        return new ConceptParseResult(concepts, problems, !complete);
    }

    /// <summary>
    /// How many items of the list are complete so far in an answer that is still arriving (their closing brace came; not checked further).
    /// For the progress on the screen ("2 of 5 ready"); <see cref="Parse"/> decides what is kept.
    /// </summary>
    public static int CompleteCount(string? answer)
    {
        var text = answer ?? string.Empty;
        var start = ArrayStart(text);
        return start < 0 ? 0 : TopLevelObjects(text, start).Objects.Count;
    }

    /// <summary>One item: as written, or after <see cref="Chat.ModelJson.Repair"/> (raw line breaks in the content, a trailing comma).</summary>
    private static JsonDocument Document(string json)
    {
        try
        {
            return JsonDocument.Parse(json, Lenient);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse(Chat.ModelJson.Repair(json), Lenient);
        }
    }

    private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private static string TextClean(string? value, int max, bool oneLine) =>
        oneLine ? Text.TextClean.OneLine(value, max) : Text.TextClean.Clean(value, max);

    private static string? Read(JsonElement root, string[] keys)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// The stored text of an item's scene list (null when it has none): "[label: on screen]" and the narration under it, scene after scene;
    /// a scene with neither is skipped.
    /// </summary>
    private static string? SceneText(JsonElement root, string label)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in SceneKeys)
        {
            if (!root.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var text = new System.Text.StringBuilder();
            foreach (var scene in list.EnumerateArray())
            {
                var visual = Unbracket(Text.TextClean.OneLine(Read(scene, VisualKeys), TMConcept.Core.Scenes.SceneSplitter.MaxVisual));
                var narration = Unbracket(Text.TextClean.Clean(Read(scene, NarrationKeys), TMConcept.Core.Scenes.SceneSplitter.MaxNarration));
                if (visual.Length == 0 && narration.Length == 0)
                {
                    continue;
                }

                text.Append('[').Append(label).Append(": ").Append(visual).Append("]\n");
                if (narration.Length > 0)
                {
                    text.Append(narration).Append('\n');
                }
            }

            var content = text.ToString().TrimEnd('\n');
            return content.Length <= ConceptLimits.ContentMax ? content : content[..ConceptLimits.ContentMax];
        }

        return null;
    }

    private static string Unbracket(string text) => text.Replace('[', '(').Replace(']', ')');

    /// <summary>The whole numbers of the first of <paramref name="keys"/> that is a list (other items skipped); none = empty.</summary>
    private static List<int> Numbers(JsonElement root, string[] keys)
    {
        var numbers = new List<int>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return numbers;
        }

        foreach (var key in keys)
        {
            if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number) && !numbers.Contains(number))
                    {
                        numbers.Add(number);
                    }
                }

                break;
            }
        }

        return numbers;
    }

    /// <summary>Index of the '[' that opens the concept list (after the list key; or the first '[' of a bare array).</summary>
    private static int ArrayStart(string text)
    {
        foreach (var key in ListKeys)
        {
            var at = text.IndexOf('"' + key + '"', StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                var bracket = text.IndexOf('[', at);
                if (bracket >= 0)
                {
                    return bracket;
                }
            }
        }

        var first = text.IndexOf('[', StringComparison.Ordinal);
        var brace = text.IndexOf('{', StringComparison.Ordinal);
        return first >= 0 && (brace < 0 || first < brace) ? first : -1;
    }

    /// <summary>The complete <c>{…}</c> objects directly inside the array; <c>complete</c> is false when the array never closed.</summary>
    private static (List<string> Objects, bool Complete) TopLevelObjects(string text, int arrayStart)
    {
        var objects = new List<string>();
        var depth = 0;
        var objectStart = -1;
        var inString = false;
        var escaped = false;
        for (var i = arrayStart + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{' or '[':
                    if (depth == 0 && c == '{')
                    {
                        objectStart = i;
                    }

                    depth++;
                    break;
                case '}' or ']':
                    if (depth == 0 && c == ']')
                    {
                        return (objects, true);
                    }

                    depth--;
                    if (depth == 0 && c == '}' && objectStart >= 0)
                    {
                        objects.Add(text[objectStart..(i + 1)]);
                        objectStart = -1;
                    }

                    break;
            }
        }

        return (objects, false);
    }
}
