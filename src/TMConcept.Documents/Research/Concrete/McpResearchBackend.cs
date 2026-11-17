using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;
using TMConcept.Core.Research;
using TMConcept.Documents.Mcp;
using TMConcept.Documents.Web;

namespace TMConcept.Documents.Research;

/// <summary>
/// A tool of the user's own MCP server as a research backend (advanced, off by default). The app calls ONLY the two tools the user chose
/// (a tool the server does not list is refused before any call): the search tool with the query, and, for each picked hit, the fetch tool
/// with its address; without a fetch tool the app fetches the address itself through the guarded web fetcher (public addresses only, the
/// site's robots.txt honoured). An address on this computer or the local network is never a hit and never goes to the fetch tool. A
/// search answer is read as a list of results when it is JSON with addresses ("url", "link", …) or text with addresses; otherwise the
/// answer itself is one hit whose text is kept as it is. Every answer is data, like a web page: cleaned, stored, and sent to the model only
/// inside the documents prompt's data block. One session per run; disposing ends it (and the server program).
/// </summary>
public sealed partial class McpResearchBackend(McpServerSettings server, McpConnector connector, WebPageFetcher web, ILogger<McpResearchBackend> logger)
    : ResearchBackendBase, IAsyncDisposable
{
    private static readonly string[] UrlNames = ["url", "link", "href", "uri", "source"];
    private static readonly string[] TitleNames = ["title", "name", "label", "headline"];
    private static readonly string[] SnippetNames = ["snippet", "description", "content", "summary", "text"];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpClient? _client;

    /// <inheritdoc />
    public override string Id => "mcp:" + server.Id.ToString("N");

    /// <inheritdoc />
    public override ResearchBackendKind Kind => ResearchBackendKind.Mcp;

    /// <inheritdoc />
    public override string Name => server.Name.Length > 0 ? server.Name : "MCP";

    /// <summary>
    /// The hits in a search tool's answer <paramref name="text"/>: the results of a JSON list (the first array of objects with an address,
    /// at any depth), else the addresses in the text ("[title](address)" or a bare address with the line's text as its title), else the
    /// answer itself as one hit titled after the query.
    /// </summary>
    public static IReadOnlyList<ResearchHit> ParseHits(string text, string name, string query)
    {
        var answer = (text ?? string.Empty).Trim();
        if (answer.Length == 0)
        {
            return [];
        }

        if (answer[0] is '{' or '[')
        {
            try
            {
                using var json = JsonDocument.Parse(answer);
                if (Results(json.RootElement, 0) is { Count: > 0 } results)
                {
                    return results;
                }
            }
            catch (JsonException)
            {
                // Text that only starts like JSON: read as text below.
            }
        }

        var hits = new List<ResearchHit>();
        foreach (var line in answer.Split('\n'))
        {
            var link = MarkdownLink().Match(line);
            if (link.Success)
            {
                if (IsPublic(link.Groups[2].Value))
                {
                    hits.Add(new ResearchHit(string.Empty, link.Groups[1].Value.Trim(), link.Groups[2].Value, string.Empty));
                }

                continue;
            }

            var bare = BareUrl().Match(line);
            if (bare.Success && bare.Value.TrimEnd('.', ',', ')', ';') is var url && IsPublic(url))
            {
                var title = line.Replace(bare.Value, string.Empty, StringComparison.Ordinal).Trim(' ', '-', '*', ':', '|', '\t', '\r');
                hits.Add(new ResearchHit(string.Empty, title.Length > 0 ? title : bare.Value, url, string.Empty));
            }
        }

        return hits.Count > 0
            ? hits.DistinctBy(h => ResearchText.UrlKey(h.Url)).ToList()
            : [new ResearchHit(string.Empty, $"{name}: {query}", string.Empty, string.Empty) { InlineText = answer }];
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_client is { } client)
        {
            _client = null;
            await client.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<ResearchHit>> SearchCoreAsync(ResearchQuery query, CancellationToken cancellationToken)
    {
        var text = await CallAsync(server.SearchTool, server.SearchArgument, query.Text, cancellationToken).ConfigureAwait(false);
        return ParseHits(text, Name, query.Text);
    }

    /// <inheritdoc />
    protected override async Task<FetchedDocument> FetchCoreAsync(ResearchHit hit, CancellationToken cancellationToken)
    {
        if (hit.InlineText.Length == 0 && server.FetchTool.Trim().Length == 0)
        {
            return await web.FetchAutomaticAsync(hit.Url, string.Empty, cancellationToken).ConfigureAwait(false);
        }

        if (hit.InlineText.Length == 0)
        {
            // The same check as the app's own fetching: a found address on this computer or the local network never goes to the fetch tool.
            var address = DocumentFetcherBase.WebAddress(hit.Url) ?? throw new DocumentRejectedException("address", "This is not a web address.", hit.Url);
            SafeHttp.RefuseLocal(address);
        }

        var text = hit.InlineText.Length > 0 ? hit.InlineText : await CallAsync(server.FetchTool, server.FetchArgument, hit.Url, cancellationToken).ConfigureAwait(false);
        if (text.Trim().Length == 0)
        {
            throw new DocumentRejectedException("not_text", "The MCP tool gave no text.", hit.Title);
        }

        var page = "# " + hit.Title + "\n\n" + text.Trim() + "\n";
        return new FetchedDocument(DocumentKind.Mcp, hit.Title, hit.Url, string.Empty, Encoding.UTF8.GetBytes(page), ".md");
    }

    private static List<ResearchHit>? Results(JsonElement element, int depth)
    {
        if (depth > 4)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var hits = element.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(Hit).OfType<ResearchHit>().ToList();
            if (hits.Count > 0)
            {
                return hits;
            }
        }

        if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            var children = element.ValueKind == JsonValueKind.Object ? element.EnumerateObject().Select(p => p.Value) : element.EnumerateArray();
            foreach (var child in children)
            {
                if (Results(child, depth + 1) is { Count: > 0 } found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static ResearchHit? Hit(JsonElement item)
    {
        string First(string[] names) => names.Select(n => item.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty)
            .FirstOrDefault(v => v.Length > 0) ?? string.Empty;
        var url = First(UrlNames);
        if (!IsPublic(url))
        {
            return null;
        }

        var title = First(TitleNames);
        return new ResearchHit(string.Empty, title.Length > 0 ? title : url, url, string.Empty) { Snippet = First(SnippetNames) };
    }

    /// <summary>
    /// Whether <paramref name="url"/> is a full http(s) address whose host is not this computer or the local network: a search answer can
    /// carry any address a page planted (a router's page, the cloud metadata address), and such a hit is never offered.
    /// </summary>
    private static bool IsPublic(string url) =>
        url.Contains("://", StringComparison.Ordinal) && DocumentFetcherBase.WebAddress(url) is { } address && !NetworkAddresses.IsLocalHost(address.Host);

    /// <summary>Calls one of the two chosen tools (only those), opening the session the first time.</summary>
    private async Task<string> CallAsync(string tool, string argument, string value, CancellationToken cancellationToken)
    {
        if (tool.Trim().Length == 0 || argument.Trim().Length == 0)
        {
            throw new DocumentRejectedException("mcp", "No tool or argument is chosen for this MCP server.", Name);
        }

        try
        {
            var client = await ClientAsync(cancellationToken).ConfigureAwait(false);
            return await client.CallToolAsync(tool.Trim(), new Dictionary<string, string>(StringComparer.Ordinal) { [argument.Trim()] = value }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (McpException ex)
        {
            logger.LogWarning("Research: MCP server {Backend} failed ({Code})", Id, ex.Code);
            throw new DocumentRejectedException("mcp", ex.Message, Name, ex);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Research: MCP server {Backend} sent a broken answer", Id);
            throw new DocumentRejectedException("mcp", "The MCP server's answer could not be read.", Name, ex);
        }
    }

    private async Task<McpClient> ClientAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is { } open)
            {
                return open;
            }

            var client = await connector.ConnectAsync(server, cancellationToken).ConfigureAwait(false);
            try
            {
                var tools = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
                foreach (var wanted in new[] { server.SearchTool, server.FetchTool }.Select(t => t.Trim()).Where(t => t.Length > 0))
                {
                    if (!tools.Any(t => t.Name == wanted))
                    {
                        throw new McpException("tool_missing", $"The MCP server has no tool named {wanted}.");
                    }
                }
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _client = client;
            return client;
        }
        finally
        {
            _gate.Release();
        }
    }

    [GeneratedRegex(@"\[([^\]\n]{1,300})\]\((https?://[^\s)]+)\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"https?://[^\s<>""'\]]+")]
    private static partial Regex BareUrl();
}
