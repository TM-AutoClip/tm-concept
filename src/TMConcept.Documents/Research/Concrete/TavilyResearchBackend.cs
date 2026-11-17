using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;
using TMConcept.Core.Keys;
using TMConcept.Core.Research;
using TMConcept.Documents.Web;

namespace TMConcept.Documents.Research;

/// <summary>
/// Tavily web search with the user's OWN key (Settings → Research; stored encrypted like every API key, sent only to Tavily's fixed address
/// in the Authorization header, never logged). Tavily is a search API made for AI apps, with a free monthly allowance. Only its result list
/// is used (titles and addresses); every page is then fetched by the app itself through the guarded web fetcher, so what is stored is the
/// page, not Tavily's copy (only when the site's robots.txt allows it). Without a key the backend is not offered.
/// </summary>
public sealed class TavilyResearchBackend(DocumentTransport transport, IApiKeyStore keys, WebPageFetcher web, ILogger<TavilyResearchBackend> logger)
    : HttpResearchBackendBase(transport, logger)
{
    /// <summary>The provider code its key is stored under.</summary>
    public const string KeyProvider = "tavily";

    /// <summary>The search address.</summary>
    public static readonly Uri Endpoint = new("https://api.tavily.com/search");

    /// <inheritdoc />
    public override string Id => KeyProvider;

    /// <inheritdoc />
    public override ResearchBackendKind Kind => ResearchBackendKind.Tavily;

    /// <inheritdoc />
    public override string Name => "Tavily";

    /// <inheritdoc />
    public override bool NeedsKey => true;

    /// <summary>The request body of a search (the key is not in it).</summary>
    public static string RequestBody(string text, int count)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("query", text);
            writer.WriteNumber("max_results", count);
            writer.WriteString("search_depth", "basic");
            writer.WriteString("topic", "general");
            writer.WriteBoolean("include_answer", false);
            writer.WriteBoolean("include_raw_content", false);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The hits of a search answer (the backend's id is filled in later).</summary>
    public static IReadOnlyList<ResearchHit> ParseSearch(JsonElement root)
    {
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var hits = new List<ResearchHit>();
        foreach (var result in results.EnumerateArray())
        {
            var url = Text(result, "url");
            if (DocumentFetcherBase.WebAddress(url) is null || !url.Contains("://", StringComparison.Ordinal))
            {
                continue;
            }

            hits.Add(new ResearchHit(string.Empty, Text(result, "title") is { Length: > 0 } title ? title : url, url, string.Empty)
            {
                Snippet = Text(result, "content"),
            });
        }

        return hits;
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<ResearchHit>> SearchCoreAsync(ResearchQuery query, CancellationToken cancellationToken)
    {
        var key = (await keys.ListAsync(KeyProvider, cancellationToken).ConfigureAwait(false)).FirstOrDefault()?.Secret ?? string.Empty;
        if (key.Trim().Length == 0)
        {
            throw new DocumentRejectedException("key", "No Tavily key is saved.", Name);
        }

        var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Authorization"] = "Bearer " + key.Trim() };
        var response = await Http.PostJsonAsync(Endpoint, RequestBody(query.Text, query.MaxHits), headers, cancellationToken).ConfigureAwait(false);
        using var json = Json(response.Body);
        return ParseSearch(json.RootElement);
    }

    /// <inheritdoc />
    protected override Task<FetchedDocument> FetchCoreAsync(ResearchHit hit, CancellationToken cancellationToken) =>
        web.FetchAutomaticAsync(hit.Url, string.Empty, cancellationToken);
}
