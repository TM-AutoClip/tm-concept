using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using TMConcept.Core.Profiles;
using TMConcept.Core.Text;

namespace TMConcept.Core.Concepts;

/// <summary>
/// Builds the concept prompt from the embedded template of the prompt language (Prompts/&lt;lang&gt;/concepts.txt: Turkish for Turkish videos,
/// English for every other output language, which the prompt names; the user's texts may be written in the interface language). The order follows the
/// cache: the fixed part (rules + channel + concept + format + sample images) comes first and carries the cache mark; the changing part
/// (history, extra request, count) comes last. The history grows after every batch, so inside the cached part it made every call write a
/// new cache entry (1.25× input price) and never read one. User text is put inside « » (rule 10) with the guillemets themselves removed from it, so it cannot close the block.
/// A line whose value is empty is left out (no images, no history, no extra request). No placeholder goes to the model: the placeholders
/// ([pause 1 s], [soft transition]…) are put into a concept's text afterwards and applied by the program itself.
/// </summary>
public static class ConceptPromptBuilder
{
    private const string Separator = "=====";

    /// <summary>Builds.</summary>
    public static ConceptPrompt Build(ConceptPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var history = input.HistoryTitles.Count == 0 ? null : string.Join("\n", input.HistoryTitles.Select(t => "- " + Data(t, ConceptLimits.TitleHardMax)));
        return Make("concepts", input.Profile, input.Images, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["history_count"] = history is null ? null : input.HistoryTitles.Count.ToString(CultureInfo.InvariantCulture),
            ["history"] = history,
            ["extra"] = string.IsNullOrWhiteSpace(input.ExtraPrompt) ? null : Data(input.ExtraPrompt, ConceptLimits.ExtraPromptMax),

            // Names from the app's own focus lists (not free text), in the prompt's language.
            ["focus"] = string.IsNullOrWhiteSpace(input.Focus) ? null : Data(input.Focus, ConceptLimits.ExtraPromptMax),
            ["count"] = input.Count.ToString(CultureInfo.InvariantCulture),
        });
    }

    /// <summary>
    /// Builds the prompt of an AI edit of one concept (Prompts/&lt;lang&gt;/concept-edit.txt): the same channel part, rules and answer format
    /// as <see cref="Build"/> (so <see cref="ConceptResponseParser"/> reads its answer), then the current title, summary and content and the
    /// user's wish. The placeholders the user already put in the text go along and the prompt says to keep them where they are.
    /// </summary>
    public static ConceptPrompt BuildEdit(ConceptEditPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // The user's texts are filled after the channel's values: a "{channel}" typed into the content stays as typed.
        return Make("concept-edit", input.Profile, input.Images, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["title"] = Data(input.Title, ConceptLimits.TitleHardMax),
            ["summary"] = Data(input.Summary, ConceptLimits.SummaryMax),
            ["instruction"] = Data(input.Instruction, ConceptLimits.ExtraPromptMax),
            ["content"] = Data(input.Content, ConceptLimits.ContentMax),
        });
    }

    /// <summary>Fills a template: the channel's values first, then <paramref name="own"/> (the values of this kind of prompt).</summary>
    private static ConceptPrompt Make(string file, Profile profile, IReadOnlyList<ProfileImage> images, Dictionary<string, string?> own)
    {
        var language = OutputLanguages.Of(profile.ContentLanguage);
        var (words, template) = Template(language.PromptLanguage, file);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["channel"] = Data(profile.Name, ProfileLimits.NameMax),
            ["concept"] = Data(profile.ConceptText, ProfileLimits.ConceptTextMax),
            // The output language by name in the prompt's language; the user's « » texts may be in another one (rule 12 of the prompt).
            ["language"] = language.PromptLanguage == ContentLanguages.Turkish ? language.Turkish : language.English,
            ["part"] = language.PartWord,
            ["format"] = profile.Format == VideoFormat.Vertical ? words["vertical"] : words["horizontal"],
            ["seconds"] = profile.TargetSeconds.ToString(CultureInfo.InvariantCulture),
            ["words"] = Words(profile.ContentLanguage, profile.TargetSeconds).ToString(CultureInfo.InvariantCulture),
            ["images"] = images.Count == 0
                ? null
                : string.Join("; ", images.Select((image, i) => string.Create(CultureInfo.InvariantCulture, $"{i + 1}: «{Data(image.Caption, ProfileLimits.CaptionMax)}»"))),
        };
        foreach (var (key, value) in own)
        {
            values[key] = value;
        }

        var split = template.IndexOf(Separator, StringComparison.Ordinal);
        return new ConceptPrompt(Fill(template[..split], values), Fill(template[(split + Separator.Length)..], values), Schema());
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

    private static string Data(string? text, int max) => TextClean.Clean(text, max).Replace("«", "\"", StringComparison.Ordinal).Replace("»", "\"", StringComparison.Ordinal);

    private static string Fill(string part, Dictionary<string, string?> values)
    {
        var output = new StringBuilder();
        foreach (var line in part.Trim('\n').Split('\n'))
        {
            var text = line;
            var drop = false;
            foreach (var (key, value) in values)
            {
                var mark = "{" + key + "}";
                if (!text.Contains(mark, StringComparison.Ordinal))
                {
                    continue;
                }

                if (value is null)
                {
                    drop = true;
                    break;
                }

                text = text.Replace(mark, value, StringComparison.Ordinal);
            }

            if (!drop)
            {
                output.Append(text).Append('\n');
            }
        }

        return output.ToString().TrimEnd('\n');
    }

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
