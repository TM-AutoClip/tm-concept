using System.Text.Json;

namespace TMConcept.Core.Concepts;

/// <summary>
/// Reads <c>{"konseptler":[{"baslik":"…","ozet":"…","icerik":"…"}]}</c> from a model answer. The answer is untrusted: a code fence or text
/// around the JSON is skipped, NUL and control characters are removed, lengths are cut. A cut-off answer (the model stopped in the middle)
/// still yields every complete item before the cut; a broken item is reported and skipped, the others are kept. English key names
/// (<c>concepts</c>, <c>title</c>, <c>summary</c>, <c>content</c>) are accepted too.
/// </summary>
public static class ConceptResponseParser
{
    private static readonly string[] ListKeys = ["konseptler", "concepts"];
    private static readonly string[] TitleKeys = ["baslik", "title"];
    private static readonly string[] SummaryKeys = ["ozet", "summary"];
    private static readonly string[] ContentKeys = ["icerik", "content"];

    /// <summary>Parses.</summary>
    public static ConceptParseResult Parse(string? answer)
    {
        var problems = new List<string>();
        var text = (answer ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal);
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
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var title = TextClean(Read(root, TitleKeys), ConceptLimits.TitleHardMax, oneLine: true);
                var summary = TextClean(Read(root, SummaryKeys), ConceptLimits.SummaryMax, oneLine: true);
                var content = TextClean(Read(root, ContentKeys), ConceptLimits.ContentMax, oneLine: false);
                if (title.Length == 0 || content.Length == 0)
                {
                    problems.Add($"Item {number}: the title or the content is empty.");
                    continue;
                }

                concepts.Add(new ParsedConcept(title, summary, content));
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
