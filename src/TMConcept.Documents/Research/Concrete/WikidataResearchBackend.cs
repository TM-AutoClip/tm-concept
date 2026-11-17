using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;
using TMConcept.Core.Research;
using TMConcept.Documents.Web;

namespace TMConcept.Documents.Research;

/// <summary>
/// Wikidata (on by default, no key; its data is CC0): items are searched by their labels in the searched language
/// (<c>wbsearchentities</c>), and an item's facts come from ONE query of the public SPARQL service: every statement whose value is an item,
/// a date, a number, a text or a place, with the property's and the value's labels in the item's language, else English. Identifiers and
/// media files are left out. The facts are kept as a short Markdown page ("- founded: 537"), one line per property. A date of 1 January at
/// midnight is shown as its year (most such dates are known to the year only). The item id is checked before it goes into the query.
/// </summary>
public sealed partial class WikidataResearchBackend(DocumentTransport transport, ILogger<WikidataResearchBackend> logger) : HttpResearchBackendBase(transport, logger)
{
    /// <inheritdoc />
    public override string Id => "wikidata";

    /// <inheritdoc />
    public override ResearchBackendKind Kind => ResearchBackendKind.Wikidata;

    /// <inheritdoc />
    public override string Name => "Wikidata";

    /// <inheritdoc />
    public override bool PerLanguage => true;

    /// <summary>The prefix of a Wikidata hit's entity key ("wikidata:Q12506").</summary>
    public const string KeyPrefix = "wikidata:";

    /// <summary>The search address of <paramref name="text"/> in <paramref name="language"/>.</summary>
    public static Uri SearchAddress(string language, string text, int count) => new(string.Create(CultureInfo.InvariantCulture,
        $"https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&maxlag=5&type=item&limit={count}&language={language}&uselang={language}&search={Uri.EscapeDataString(text)}"));

    /// <summary>The SPARQL query of the facts of <paramref name="item"/> with labels in <paramref name="language"/> (then English); null when the id is not an item id.</summary>
    public static string? FactsQuery(string item, string language)
    {
        if (!Item().IsMatch(item ?? string.Empty) || ResearchText.Language(language) is not { } code)
        {
            return null;
        }

        return string.Create(CultureInfo.InvariantCulture, $$"""
            SELECT ?propLabel ?value ?valueLabel WHERE {
              wd:{{item}} ?direct ?value .
              ?prop wikibase:directClaim ?direct ; wikibase:propertyType ?type .
              FILTER(?type IN (wikibase:WikibaseItem, wikibase:Time, wikibase:Quantity, wikibase:String, wikibase:Monolingualtext, wikibase:GlobeCoordinate))
              SERVICE wikibase:label { bd:serviceParam wikibase:language "{{code}},en". }
            } LIMIT {{ResearchLimits.FactsMax}}
            """);
    }

    /// <summary>The hits of a search answer (the backend's id is filled in later).</summary>
    public static IReadOnlyList<ResearchHit> ParseSearch(JsonElement root, string language)
    {
        if (!root.TryGetProperty("search", out var search) || search.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var hits = new List<ResearchHit>();
        foreach (var entry in search.EnumerateArray())
        {
            var id = Text(entry, "id");
            var label = Text(entry, "label");
            if (!Item().IsMatch(id) || label.Length == 0)
            {
                continue;
            }

            hits.Add(new ResearchHit(string.Empty, label, "https://www.wikidata.org/wiki/" + id, language)
            {
                Snippet = Text(entry, "description"),
                EntityKey = KeyPrefix + id,
            });
        }

        return hits;
    }

    /// <summary>The facts page of a SPARQL answer for the item titled <paramref name="title"/>; empty when it holds no fact.</summary>
    public static string ParseFacts(JsonElement root, string title, string description)
    {
        var facts = new List<(string Property, List<string> Values)>();
        if (root.TryGetProperty("results", out var results) && results.TryGetProperty("bindings", out var bindings) && bindings.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in bindings.EnumerateArray())
            {
                var property = Value(row, "propLabel");
                var value = Shown(Value(row, "valueLabel") is { Length: > 0 } label ? label : Value(row, "value"));
                if (property.Length == 0 || value.Length == 0 || Item().IsMatch(property) || Property().IsMatch(property))
                {
                    continue;
                }

                var line = facts.FirstOrDefault(f => f.Property == property);
                if (line.Values is null)
                {
                    facts.Add(line = (property, []));
                }

                if (!line.Values.Contains(value, StringComparer.Ordinal))
                {
                    line.Values.Add(value);
                }
            }
        }

        if (facts.Count == 0)
        {
            return string.Empty;
        }

        var page = new StringBuilder("# ").Append(title).Append("\n\n");
        if (description.Length > 0)
        {
            page.Append(description).Append("\n\n");
        }

        page.Append("## Wikidata\n\n");
        foreach (var (property, values) in facts)
        {
            page.Append("- ").Append(property).Append(": ").Append(string.Join("; ", values)).Append('\n');
        }

        return page.ToString();
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<ResearchHit>> SearchCoreAsync(ResearchQuery query, CancellationToken cancellationToken)
    {
        var language = ResearchText.Language(query.Language) ?? "en";
        var response = await Http.GetAsync(SearchAddress(language, query.Text, query.MaxHits), cancellationToken).ConfigureAwait(false);
        using var json = Json(response.Body);
        return ParseSearch(json.RootElement, language);
    }

    /// <inheritdoc />
    protected override async Task<FetchedDocument> FetchCoreAsync(ResearchHit hit, CancellationToken cancellationToken)
    {
        var item = hit.EntityKey.StartsWith(KeyPrefix, StringComparison.Ordinal) ? hit.EntityKey[KeyPrefix.Length..] : string.Empty;
        var sparql = FactsQuery(item, hit.Language) ?? throw new DocumentRejectedException("not_found", "This is not a Wikidata item.", hit.Title);
        var response = await Http.GetAsync(new Uri("https://query.wikidata.org/sparql?format=json&query=" + Uri.EscapeDataString(sparql)), cancellationToken)
            .ConfigureAwait(false);
        using var json = Json(response.Body);
        var page = ParseFacts(json.RootElement, hit.Title, hit.Snippet);
        if (page.Length == 0)
        {
            throw new DocumentRejectedException("not_found", "Wikidata has no facts about this item.", hit.Title);
        }

        return new FetchedDocument(DocumentKind.Wikidata, hit.Title, hit.Url, hit.Language, Encoding.UTF8.GetBytes(page), ".md");
    }

    private static string Value(JsonElement row, string name) =>
        row.TryGetProperty(name, out var cell) ? Text(cell, "value").Trim() : string.Empty;

    /// <summary>A value as shown: an entity without a label is left out, a date loses its midnight (and its 1 January).</summary>
    private static string Shown(string value)
    {
        if (value.StartsWith("http://www.wikidata.org/", StringComparison.Ordinal) || Item().IsMatch(value))
        {
            return string.Empty;
        }

        var date = Date().Match(value);
        if (!date.Success)
        {
            return value;
        }

        return date.Groups[2].Value == "01-01" ? date.Groups[1].Value : date.Groups[1].Value + "-" + date.Groups[2].Value;
    }

    [GeneratedRegex(@"^Q[1-9][0-9]{0,11}$")]
    private static partial Regex Item();

    [GeneratedRegex(@"^P[1-9][0-9]{0,11}$")]
    private static partial Regex Property();

    [GeneratedRegex(@"^([+-]?\d{1,6})-(\d{2}-\d{2})T00:00:00Z$")]
    private static partial Regex Date();
}
