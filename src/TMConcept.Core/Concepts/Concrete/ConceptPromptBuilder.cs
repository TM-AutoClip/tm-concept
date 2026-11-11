using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// Builds the prompts of concepts, AI edit and topic suggestions from the embedded templates (Prompts/&lt;lang&gt;/*.txt). Concepts and AI
/// edit: Turkish for Turkish videos, English for every other output language, which the prompt names (the user's texts may be in the
/// interface language). Topics: the interface language, since the user reads and picks the topics; a selected topic later goes to the
/// concept prompt as one line of its TOPICS block (one concept each, in order). The order follows the
/// cache: the fixed part (rules + channel content + format + sample images) comes first and carries the cache mark; the changing part (the
/// profile's past, see <see cref="PromptMemoryBuilder"/>, the wishes, the extra request, the topics, the count) comes last, so a new concept never
/// changes the cached part (inside it the growing history made every call write a new cache entry at 1.25× and never read one). The
/// channel's NAME is not sent (10 Nov 2026, the developer: "the name does not matter, the channel content does"): it is only the user's
/// label; the channel content is the most important input and every concept and topic must stay inside it. User text is put inside « »
/// with the guillemets themselves removed from it, so it cannot close the block (<see cref="PromptText"/>). A line whose value is empty is
/// left out (no images, no history, no extra request). No placeholder ([pause 1 s], [soft transition]…) goes to the concept model: they are
/// put into a concept's text afterwards and applied by the program itself. The prompts are not shown to the user and cannot be edited.
/// </summary>
public static partial class ConceptPromptBuilder
{
    private const string Separator = "=====";

    /// <summary>Builds.</summary>
    public static ConceptPrompt Build(ConceptPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (words, template) = Template(OutputLanguages.Of(input.Profile.ContentLanguage).PromptLanguage, "concepts");
        var values = Channel(input.Profile, input.Images, words);
        Memory(values, input.Memory);
        values["wishes"] = input.Memory.Wishes;
        values["extra"] = string.IsNullOrWhiteSpace(input.ExtraPrompt) ? null : PromptText.Data(input.ExtraPrompt, ConceptLimits.ExtraPromptMax);

        // Names from the app's own focus lists (not free text), in the prompt's language.
        values["focus"] = string.IsNullOrWhiteSpace(input.Focus) ? null : PromptText.Data(input.Focus, ConceptLimits.ExtraPromptMax);

        // The selected topics of this batch, numbered in order (one concept each); no topics, no line.
        values["topics"] = input.Topics is not { Count: > 0 } topics
            ? null
            : string.Concat(topics.Select((t, i) => string.Create(CultureInfo.InvariantCulture, $"\n{i + 1}. «{PromptText.Line(t, TopicLimits.TopicMax)}»")));
        values["count"] = input.Count.ToString(CultureInfo.InvariantCulture);
        return Make(template, values, Schema());
    }

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
        var values = Channel(input.Profile, input.Images, words);
        values["title"] = PromptText.Data(input.Title, ConceptLimits.TitleHardMax);
        values["summary"] = PromptText.Data(input.Summary, ConceptLimits.SummaryMax);
        values["instruction"] = PromptText.Data(input.Instruction, ConceptLimits.ExtraPromptMax);
        values["content"] = PromptText.Data(input.Content, ConceptLimits.ContentMax);
        return Make(template, values, Schema());
    }

    /// <summary>
    /// Builds the "Suggest topics" prompt (Prompts/&lt;interface lang&gt;/topics.txt; a language other than Turkish gets the English one): the
    /// channel content, its format and target length (so a topic fits one video), the video language, the profile's covered concepts and
    /// topic dislikes, the newest earlier suggestions, the selected topics and the focus. The extra request is not sent (0.1.12): the old
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
        values["suggested"] = Lines(input.EarlierSuggestions);
        values["selected"] = Lines(input.Selected);
        values["focus"] = string.IsNullOrWhiteSpace(input.Focus) ? null : PromptText.Data(input.Focus, ConceptLimits.ExtraPromptMax);
        values["count"] = input.Count.ToString(CultureInfo.InvariantCulture);
        return Make(template, values, TopicSchema());
    }

    /// <summary>Topics as "- " lines under their heading; none = null, so the heading's line is left out.</summary>
    private static string? Lines(IReadOnlyList<string> topics) =>
        topics.Count == 0 ? null : string.Concat(topics.Select(s => "\n- " + PromptText.Line(s, TopicLimits.TopicMax)));

    /// <summary>The channel part of the concept and edit prompts (no name: see the class summary).</summary>
    private static Dictionary<string, string?> Channel(Profile profile, IReadOnlyList<ProfileImage> images, Dictionary<string, string> words)
    {
        var language = OutputLanguages.Of(profile.ContentLanguage);
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["concept"] = PromptText.Data(profile.ConceptText, ProfileLimits.ConceptTextMax),
            // The output language by name in the prompt's language; the user's « » texts may be in another one (the language rule).
            ["language"] = language.PromptLanguage == ContentLanguages.Turkish ? language.Turkish : language.English,
            ["part"] = language.PartWord,
            ["format"] = profile.Format == VideoFormat.Vertical ? words["vertical"] : words["horizontal"],
            ["seconds"] = profile.TargetSeconds.ToString(CultureInfo.InvariantCulture),
            ["words"] = Words(profile.ContentLanguage, profile.TargetSeconds).ToString(CultureInfo.InvariantCulture),
            ["images"] = images.Count == 0
                ? null
                : string.Join("; ", images.Select((image, i) => string.Create(CultureInfo.InvariantCulture, $"{i + 1}: «{PromptText.Data(image.Caption, ProfileLimits.CaptionMax)}»"))),
        };
    }

    /// <summary>The profile's past: covered (with its count), topic and telling dislikes.</summary>
    private static void Memory(Dictionary<string, string?> values, PromptMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        values["covered_count"] = memory.Covered is null ? null : memory.CoveredCount.ToString(CultureInfo.InvariantCulture);
        values["covered"] = memory.Covered;
        values["disliked_topics"] = memory.DislikedTopics;
        values["disliked_telling"] = memory.DislikedTelling;
    }

    private static ConceptPrompt Make(string template, Dictionary<string, string?> values, JsonObject schema)
    {
        var split = template.IndexOf(Separator, StringComparison.Ordinal);
        return new ConceptPrompt(Fill(template[..split], values), Fill(template[(split + Separator.Length)..], values), schema);
    }

    /// <summary>Narration words for a target length (estimated speaking rate of the output language).</summary>
    public static int Words(string language, int seconds) => (int)Math.Round(seconds * OutputLanguages.Of(language).WordsPerSecond);

    /// <summary>The strict answer schema: every field required, no extra fields.</summary>
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
