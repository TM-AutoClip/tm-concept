using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TMConcept.Core.Documents;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// Builds the prompts of concepts, AI edit and topic suggestions from the embedded templates (Prompts/&lt;lang&gt;/*.txt). Concepts and AI
/// edit: Turkish for Turkish videos, English for every other output language, which the prompt names (the user's texts may be in the
/// interface language). Topics: the interface language, since the user reads and ticks the topics; a ticked topic later goes to the
/// concept prompt as one line of its TOPICS block (one concept each, in order; see <see cref="TopicBrief"/>). The order follows the
/// cache: the fixed part (rules + channel content + format) comes first and carries the cache mark; the changing part (the
/// profile's past, see <see cref="PromptMemoryBuilder"/>, the wishes, the extra request, the topics, the count) comes last, so a new concept never
/// changes the cached part (inside it the growing history made every call write a new cache entry at 1.25× and never read one). The
/// channel's NAME is not sent (10 Nov 2026, the developer: "the name does not matter, the channel content does"): it is only the user's
/// label; the channel content is the most important input and every concept and topic must stay inside it. User text is put inside « »
/// with the guillemets themselves removed from it, so it cannot close the block (<see cref="PromptText"/>). A line whose value is empty is
/// left out (no history, no extra request). No placeholder ([pause 1 s], [soft transition]…) goes to the concept model: they are
/// put into a concept's text afterwards and applied by the program itself. The prompts are not shown to the user and cannot be edited.
/// A documents run (0.1.21, a <see cref="ConceptPromptInput.Grounding"/>) uses its own prompt (concepts-documents.txt): only the passages'
/// facts, the documents named in "kaynaklar", the concepts in the video's language whatever the documents' language; its passages are in
/// the changing part, so the cached part stays the same for the whole run. Its version is the file's <c>@version</c> line
/// (<see cref="DocumentPromptVersion"/>).
/// 20 Nov 2026 (the developer: "get the scenes from the prompt as JSON so they can be checked"): a generated concept's answer gives its scenes
/// as a list (<see cref="SceneSchema"/>); the scene count comes from the target length (<see cref="ConceptSceneCheck.Target"/>) and is stated in
/// the fixed part (it depends on the profile only, so the cache holds). The count stays in the prompt, never in the schema (no minItems:
/// Anthropic's structured outputs accept no minItems other than 0 or 1); <see cref="ConceptSceneCheck"/> checks it in code and a concept that
/// fails gets one repair request (<see cref="BuildRepair"/>). An AI edit keeps its free text (<see cref="Schema"/>): the user's placeholders
/// stay where they stand in it.
/// </summary>
public static partial class ConceptPromptBuilder
{
    private const string Separator = "=====";

    /// <summary>Builds.</summary>
    public static ConceptPrompt Build(ConceptPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (words, template) = Template(OutputLanguages.Of(input.Profile.ContentLanguage).PromptLanguage,
            input.Grounding is null ? "concepts" : "concepts-documents");
        var values = Channel(input.Profile, words);
        Memory(values, input.Memory);
        values["wishes"] = input.Memory.Wishes;
        values["extra"] = string.IsNullOrWhiteSpace(input.ExtraPrompt) ? null : PromptText.Data(input.ExtraPrompt, ConceptLimits.ExtraPromptMax);

        // Names from the app's own focus lists (not free text), in the prompt's language.
        values["focus"] = string.IsNullOrWhiteSpace(input.Focus) ? null : PromptText.Data(input.Focus, ConceptLimits.ExtraPromptMax);

        // The ticked topics of this batch, numbered in order (one concept each); an idea brief has none, so no line.
        values["topics"] = input.Topics is not { Count: > 0 } topics
            ? null
            : string.Concat(topics.Select((t, i) => string.Create(CultureInfo.InvariantCulture, $"\n{i + 1}. «{PromptText.Line(t, TopicLimits.TopicMax)}»")));
        values["count"] = input.Count.ToString(CultureInfo.InvariantCulture);
        if (input.Grounding is { } grounding)
        {
            values["documents"] = DocumentLines(grounding);
            values["sources"] = PassageLines(grounding);
            return Make(template, values, DocumentSchema());
        }

        return Make(template, values, SceneSchema());
    }

    /// <summary>
    /// The user part of the one repair request of a concept batch (Prompts/&lt;lang&gt;/concept-repair.txt; the system part is the batch's own,
    /// so the cache is read): every concept that failed <see cref="ConceptSceneCheck"/> with its concrete findings ("3 scenes, at least 5
    /// needed; scene 2 has an empty on-screen line") and its current text as data, and the answer format of the batch. A documents run
    /// (<paramref name="grounding"/>) gets the batch's documents and passages again with the rule to use only their facts and "kaynaklar" in
    /// the format: they are in the batch's user part, which the repair does not repeat, and a concept split into more scenes must not be padded
    /// with facts from elsewhere (the 0.1.30 review). Every title is asked back unchanged: the answers are matched to the concepts by it
    /// (<see cref="ConceptRepairMatch"/>).
    /// </summary>
    public static string BuildRepair(Profile profile, IReadOnlyList<ConceptRepairItem> items, DocumentGrounding? grounding)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(items);
        var (words, template) = Template(OutputLanguages.Of(profile.ContentLanguage).PromptLanguage, "concept-repair");
        var c = CultureInfo.InvariantCulture;
        var list = new StringBuilder();
        for (var i = 0; i < items.Count; i++)
        {
            var problems = string.Join("; ", items[i].Problems.Select(p => Finding(words, p)));
            list.Append(c, $"\n{i + 1}. «{PromptText.Line(items[i].Concept.Title, ConceptLimits.TitleHardMax)}»\n")
                .Append(words["problems"]).Append(": ").Append(problems).Append('\n')
                .Append(words["current"]).Append(":\n«").Append(PromptText.Data(items[i].Concept.Content, ConceptLimits.ContentMax)).Append('»');
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["items"] = list.ToString(),
            ["count"] = items.Count.ToString(c),
            ["json"] = grounding is not null ? words["json_documents"] : words["json"],
            ["documents_rule"] = grounding is not null ? words["documents_rule"] : null,
            ["documents"] = grounding is not null ? DocumentLines(grounding) : null,
            ["sources"] = grounding is not null ? PassageLines(grounding) : null,
        };
        Scenes(values, profile);
        return Fill(template, values);
    }

    /// <summary>One finding in the prompt's language (the template's @few / @empty / @long line with its numbers).</summary>
    private static string Finding(Dictionary<string, string> words, SceneProblem problem) => problem.Kind switch
    {
        SceneProblemKind.TooFew => string.Format(CultureInfo.InvariantCulture, words["few"], problem.Have, problem.Need, problem.Target),
        SceneProblemKind.EmptyVisual => string.Format(CultureInfo.InvariantCulture, words["empty"], problem.Scene),
        _ => string.Format(CultureInfo.InvariantCulture, words["long"], problem.Scene, problem.Have, problem.Need),
    };

    /// <summary>The scene values of a prompt: the target count, the fewest accepted, seconds per scene and the most words of one scene.</summary>
    private static void Scenes(Dictionary<string, string?> values, Profile profile)
    {
        var c = CultureInfo.InvariantCulture;
        values["scenes"] = ConceptSceneCheck.Target(profile.TargetSeconds).ToString(c);
        values["min_scenes"] = ConceptSceneCheck.Minimum(profile.TargetSeconds).ToString(c);
        values["scene_seconds"] = SceneTargets.SecondsPerScene.ToString("0.#", c);
        values["max_scene_seconds"] = SceneTargets.MaxSceneSeconds.ToString("0", c);
        values["scene_words"] = ConceptSceneCheck.MaxSceneWords(profile.ContentLanguage).ToString(c);
    }

    /// <summary>
    /// Builds the "Find topics" prompt of the documents section (Prompts/&lt;interface lang&gt;/topics-documents.txt): the channel content and
    /// format, the profile's covered concepts, its disliked topics and its disliked and liked videos (the suggestion memory of 0.1.18), the
    /// optional subject, the documents and the passages picked for the call; the answer is <see cref="DocumentTopicSchema"/>, topics in the
    /// interface language, each with the numbers of its documents and a few search words in the documents' language.
    /// </summary>
    public static ConceptPrompt BuildDocumentTopics(DocumentTopicPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var code = input.Language == ContentLanguages.Turkish ? ContentLanguages.Turkish : ContentLanguages.English;
        var (words, template) = Template(code, "topics-documents");
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["concept"] = PromptText.Data(input.Profile.ConceptText, ProfileLimits.ConceptTextMax),
            ["format"] = input.Profile.Format == VideoFormat.Vertical ? words["vertical"] : words["horizontal"],
            ["seconds"] = input.Profile.TargetSeconds.ToString(CultureInfo.InvariantCulture),
            ["subject"] = string.IsNullOrWhiteSpace(input.Subject) ? null : PromptText.Data(input.Subject, ConceptLimits.ExtraPromptMax),
            ["documents"] = DocumentLines(input.Grounding),
            ["sources"] = PassageLines(input.Grounding),
            ["count"] = input.Count.ToString(CultureInfo.InvariantCulture),
        };
        Memory(values, input.Memory);
        return Make(template, values, DocumentTopicSchema());
    }

    /// <summary>
    /// The version of the documents prompts (the <c>@version</c> line of concepts-documents.txt; both languages carry the same one, a test
    /// holds them together). It goes into the log of every documents run.
    /// </summary>
    public static string DocumentPromptVersion(string language) =>
        Template(language == ContentLanguages.Turkish ? ContentLanguages.Turkish : ContentLanguages.English, "concepts-documents").Words.GetValueOrDefault("version", "?");

    /// <summary>The documents of a grounded prompt, one per line ("1: «title»"); none = null (the line is left out).</summary>
    private static string? DocumentLines(DocumentGrounding grounding) => grounding.Documents.Count == 0
        ? null
        : string.Concat(grounding.Documents.Select(d => string.Create(CultureInfo.InvariantCulture, $"\n{d.Number}: «{PromptText.Line(d.Title, DocumentLimits.TitleMax)}»")));

    /// <summary>
    /// The passages of a grounded prompt ("[2] «p. 12 · Trade»: «text»"): the heading comes from the document too, so it is guarded as data
    /// like the text (a heading that gives orders is only a heading); none = null.
    /// </summary>
    private static string? PassageLines(DocumentGrounding grounding) => grounding.Passages.Count == 0
        ? null
        : string.Concat(grounding.Passages.Select(p => string.Create(CultureInfo.InvariantCulture,
            $"\n[{p.Document}]{(p.Locator.Length > 0 ? " «" + PromptText.Line(p.Locator, DocumentLimits.TitleMax) + "»" : string.Empty)}: «{PromptText.Data(p.Text, DocumentLimits.ChunkMaxCharacters * 2)}»")));

    /// <summary>
    /// Builds the prompt of an AI edit of one concept (Prompts/&lt;lang&gt;/concept-edit.txt): the same channel part, rules and answer format
    /// as <see cref="Build"/> (so <see cref="ConceptResponseParser"/> reads its answer), then the current title, summary and content and the
    /// user's wish. The placeholders the user already put in the text go along and the prompt says to keep them where they are.
    /// </summary>
    public static ConceptPrompt BuildEdit(ConceptEditPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (words, template) = Template(OutputLanguages.Of(input.Profile.ContentLanguage).PromptLanguage, "concept-edit");

        // A "{concept}" typed into the content stays as typed (Fill does not search a value again).
        var values = Channel(input.Profile, words);
        values["title"] = PromptText.Data(input.Title, ConceptLimits.TitleHardMax);
        values["summary"] = PromptText.Data(input.Summary, ConceptLimits.SummaryMax);
        values["instruction"] = PromptText.Data(input.Instruction, ConceptLimits.ExtraPromptMax);
        values["content"] = PromptText.Data(input.Content, ConceptLimits.ContentMax);
        return Make(template, values, Schema());
    }

    /// <summary>
    /// Builds the "Suggest topics" prompt (Prompts/&lt;interface lang&gt;/topics.txt; a language other than Turkish gets the English one): the
    /// channel content, its format and target length (so a topic fits one video), the video language, the profile's covered concepts, topic
    /// dislikes and rated videos, the newest suggestions that were shown but not picked (they may come again) and the focus. The extra request is not sent (0.1.12): the old
    /// prompt followed it even outside the channel's scope.
    /// </summary>
    public static ConceptPrompt BuildTopics(TopicPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var code = input.Language == ContentLanguages.Turkish ? ContentLanguages.Turkish : ContentLanguages.English;
        var (words, template) = Template(code, "topics");
        var video = OutputLanguages.Of(input.Profile.ContentLanguage);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["concept"] = PromptText.Data(input.Profile.ConceptText, ProfileLimits.ConceptTextMax),
            ["video_language"] = code == ContentLanguages.Turkish ? video.Turkish : video.English,
            ["format"] = input.Profile.Format == VideoFormat.Vertical ? words["vertical"] : words["horizontal"],
            ["seconds"] = input.Profile.TargetSeconds.ToString(CultureInfo.InvariantCulture),
        };
        Memory(values, input.Memory);
        values["shown"] = Lines(input.RecentlyShown);
        values["focus"] = string.IsNullOrWhiteSpace(input.Focus) ? null : PromptText.Data(input.Focus, ConceptLimits.ExtraPromptMax);
        values["count"] = input.Count.ToString(CultureInfo.InvariantCulture);
        return Make(template, values, TopicSchema());
    }

    /// <summary>Topics as "- " lines under their heading; none = null, so the heading's line is left out.</summary>
    private static string? Lines(IReadOnlyList<string> topics) =>
        topics.Count == 0 ? null : string.Concat(topics.Select(s => "\n- " + PromptText.Line(s, TopicLimits.TopicMax)));

    /// <summary>The channel part of the concept and edit prompts (no name: see the class summary).</summary>
    private static Dictionary<string, string?> Channel(Profile profile, Dictionary<string, string> words)
    {
        var language = OutputLanguages.Of(profile.ContentLanguage);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["concept"] = PromptText.Data(profile.ConceptText, ProfileLimits.ConceptTextMax),
            // The output language by name in the prompt's language; the user's « » texts may be in another one (the language rule).
            ["language"] = language.PromptLanguage == ContentLanguages.Turkish ? language.Turkish : language.English,
            ["part"] = language.PartWord,
            ["format"] = profile.Format == VideoFormat.Vertical ? words["vertical"] : words["horizontal"],
            ["seconds"] = profile.TargetSeconds.ToString(CultureInfo.InvariantCulture),
            ["words"] = Words(profile.ContentLanguage, profile.TargetSeconds).ToString(CultureInfo.InvariantCulture),
        };
        Scenes(values, profile);
        return values;
    }

    /// <summary>The profile's past: covered (with its count), topic and telling dislikes, disliked and liked videos.</summary>
    private static void Memory(Dictionary<string, string?> values, PromptMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        values["covered_count"] = memory.Covered is null ? null : memory.CoveredCount.ToString(CultureInfo.InvariantCulture);
        values["covered"] = memory.Covered;
        values["disliked_topics"] = memory.DislikedTopics;
        values["disliked_telling"] = memory.DislikedTelling;
        values["disliked_videos"] = memory.DislikedVideos;
        values["liked_videos"] = memory.LikedVideos;
    }

    private static ConceptPrompt Make(string template, Dictionary<string, string?> values, JsonObject schema)
    {
        var split = template.IndexOf(Separator, StringComparison.Ordinal);
        return new ConceptPrompt(Fill(template[..split], values), Fill(template[(split + Separator.Length)..], values), schema);
    }

    /// <summary>Narration words for a target length (estimated speaking rate of the output language).</summary>
    public static int Words(string language, int seconds) => (int)Math.Round(seconds * OutputLanguages.Of(language).WordsPerSecond);

    /// <summary>
    /// The answer schema of generated concepts: title, summary and the scene list (<c>{"ekranda": "…", "seslendirme": "…"}</c> each); every
    /// field required, no extra fields. No minItems (see the class summary).
    /// </summary>
    public static JsonObject SceneSchema() => List(SceneItem(false));

    /// <summary>One generated concept of <see cref="SceneSchema"/>; <paramref name="sources"/>: with "kaynaklar" (a documents run).</summary>
    private static JsonObject SceneItem(bool sources)
    {
        var scene = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["ekranda"] = new JsonObject { ["type"] = "string" },
                ["seslendirme"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("ekranda", "seslendirme"),
            ["additionalProperties"] = false,
        };
        var properties = new JsonObject
        {
            ["baslik"] = new JsonObject { ["type"] = "string" },
            ["ozet"] = new JsonObject { ["type"] = "string" },
            ["sahneler"] = new JsonObject { ["type"] = "array", ["items"] = scene },
        };
        var required = sources ? new JsonArray("baslik", "ozet", "sahneler", "kaynaklar") : new JsonArray("baslik", "ozet", "sahneler");
        if (sources)
        {
            properties["kaynaklar"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
        };
    }

    /// <summary>The answer object around a concept item: <c>{"konseptler": [item…]}</c>.</summary>
    private static JsonObject List(JsonObject item) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["konseptler"] = new JsonObject { ["type"] = "array", ["items"] = item } },
        ["required"] = new JsonArray("konseptler"),
        ["additionalProperties"] = false,
    };

    /// <summary>The answer schema of an AI edit: title, summary and the free text with its placeholders; every field required, no extra fields.</summary>
    public static JsonObject Schema()
    {
        var item = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["baslik"] = new JsonObject { ["type"] = "string" },
                ["ozet"] = new JsonObject { ["type"] = "string" },
                ["icerik"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("baslik", "ozet", "icerik"),
            ["additionalProperties"] = false,
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["konseptler"] = new JsonObject { ["type"] = "array", ["items"] = item } },
            ["required"] = new JsonArray("konseptler"),
            ["additionalProperties"] = false,
        };
    }

    /// <summary>The answer schema of a documents run: <see cref="SceneSchema"/> with "kaynaklar", the numbers of the documents each concept uses.</summary>
    public static JsonObject DocumentSchema() => List(SceneItem(true));

    /// <summary>
    /// The answer schema of "Find topics" in documents: <c>{"topics": [{"topic": "…", "documents": [1], "keywords": ["…"]}]}</c> (the keywords
    /// in the documents' language find the topic's passages again).
    /// </summary>
    public static JsonObject DocumentTopicSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["topics"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["topic"] = new JsonObject { ["type"] = "string" },
                        ["documents"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } },
                        ["keywords"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                    },
                    ["required"] = new JsonArray("topic", "documents", "keywords"),
                    ["additionalProperties"] = false,
                },
            },
        },
        ["required"] = new JsonArray("topics"),
        ["additionalProperties"] = false,
    };

    /// <summary>
    /// The answer schema of the topic suggestions: <c>{"scope": "…", "topics": ["…"]}</c>. The scope comes first and is required: writing
    /// the channel's scope in one sentence before the list anchors the model to the channel for a few output tokens (it is only logged). No
    /// minItems/maxItems: Anthropic's structured outputs accept no minItems other than 0 or 1, so the count stays in the prompt and the
    /// parser keeps at most that many.
    /// </summary>
    public static JsonObject TopicSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["scope"] = new JsonObject { ["type"] = "string" },
            ["topics"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        },
        ["required"] = new JsonArray("scope", "topics"),
        ["additionalProperties"] = false,
    };

    /// <summary>
    /// Fills one part of a template line by line, in one pass over the template's own placeholders: a value is never searched again, so a
    /// "{count}" typed into a user's text or a title stays as typed. A line holding a placeholder without a value is left out.
    /// </summary>
    private static string Fill(string part, Dictionary<string, string?> values)
    {
        var output = new StringBuilder();
        foreach (var line in part.Trim('\n').Split('\n'))
        {
            var drop = Placeholder().Matches(line).Any(m => values.TryGetValue(m.Groups["key"].Value, out var value) && value is null);
            if (!drop)
            {
                output.Append(Placeholder().Replace(line, m => values.TryGetValue(m.Groups["key"].Value, out var value) && value is not null ? value : m.Value))
                    .Append('\n');
            }
        }

        return output.ToString().TrimEnd('\n');
    }

    [GeneratedRegex(@"\{(?<key>[a-z_]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    private static (Dictionary<string, string> Words, string Template) Template(string code, string file)
    {
        using var stream = typeof(ConceptPromptBuilder).Assembly.GetManifestResourceStream($"TMConcept.Prompts.{code}.{file}.txt")
            ?? throw new InvalidOperationException($"The {file} prompt for '{code}' is missing from the build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var words = new Dictionary<string, string>(StringComparer.Ordinal);
        var body = new StringBuilder();
        foreach (var line in reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith('@') && line.IndexOf('=', StringComparison.Ordinal) is var eq and > 1)
            {
                words[line[1..eq]] = line[(eq + 1)..];
            }
            else
            {
                body.Append(line).Append('\n');
            }
        }

        return (words, body.ToString());
    }
}
