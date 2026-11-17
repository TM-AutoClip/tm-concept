using System.Text.Json.Nodes;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Text;

namespace TMConcept.Core.Documents;

/// <summary>
/// Reads the answer of "Find topics" in documents (<c>{"topics": [{"topic": "…", "documents": [1]}]}</c>; a plain string item counts too):
/// one line each, no repeats (folded), at most <c>count</c>; the document numbers become the titles of the documents the call held, unknown
/// numbers are dropped; each topic keeps the ids of those documents and up to <see cref="KeywordsMax"/> search words (in the documents'
/// language; an answer without them is still read).
/// </summary>
public static class DocumentTopicParser
{
    /// <summary>Search words kept of one topic, at most.</summary>
    public const int KeywordsMax = 5;

    /// <summary>The longest search word or name kept.</summary>
    public const int KeywordMax = 60;

    /// <summary>The topics of <paramref name="answer"/>; null when it holds no topic list at all (not readable).</summary>
    public static IReadOnlyList<DocumentTopic>? Parse(string? answer, DocumentGrounding grounding, int count)
    {
        ArgumentNullException.ThrowIfNull(grounding);
        var node = ModelJson.TryParse(answer);
        var list = node switch
        {
            JsonObject root when root["topics"] is JsonArray items => items,
            JsonArray bare => bare,
            _ => null,
        };
        if (list is null)
        {
            return null;
        }

        var topics = new List<DocumentTopic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in list)
        {
            string? text = null;
            var numbers = new List<int>();
            var keywords = new List<string>();
            if (item is JsonValue value && value.TryGetValue<string>(out var plain))
            {
                text = plain;
            }
            else if (item is JsonObject entry)
            {
                text = entry["topic"] is JsonValue topic && topic.TryGetValue<string>(out var t) ? t : null;
                if (entry["documents"] is JsonArray documents)
                {
                    numbers.AddRange(documents.OfType<JsonValue>().Select(d => d.TryGetValue<int>(out var n) ? n : 0).Where(n => n > 0));
                }

                if (entry["keywords"] is JsonArray words)
                {
                    keywords.AddRange(words.OfType<JsonValue>().Select(w => w.TryGetValue<string>(out var k) ? TextClean.OneLine(k, KeywordMax) : string.Empty)
                        .Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).Take(KeywordsMax));
                }
            }

            var clean = TextClean.OneLine(text, TopicLimits.TopicMax);
            if (clean.Length == 0 || !seen.Add(TurkishFold.Fold(clean)))
            {
                continue;
            }

            var ids = numbers.Distinct().Select(n => grounding.Documents.FirstOrDefault(d => d.Number == n)?.Id).OfType<Guid>().ToList();
            topics.Add(new DocumentTopic(clean, grounding.Titles(numbers)) { DocumentIds = ids, Keywords = keywords });
            if (topics.Count == count)
            {
                break;
            }
        }

        return topics;
    }
}
