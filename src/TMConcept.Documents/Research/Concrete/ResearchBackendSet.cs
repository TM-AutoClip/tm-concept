using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;
using TMConcept.Core.Research;
using TMConcept.Core.Settings;
using TMConcept.Documents.Mcp;
using TMConcept.Documents.Web;

namespace TMConcept.Documents.Research;

/// <summary>
/// The backends one research run asks, from the user's research settings: Wikipedia and Wikidata when switched on (the default), the
/// addresses backend always (it only reads the addresses the user gave), Tavily only when it is on AND a key is saved, and the user's MCP
/// servers only when MCP is switched on (advanced, off by default) and each one is enabled and confirmed. An MCP backend holds a session:
/// <see cref="DisposeAsync"/> ends them after the run.
/// </summary>
public sealed class ResearchBackendSet(WikipediaResearchBackend wikipedia, WikidataResearchBackend wikidata, UrlResearchBackend urls, TavilyResearchBackend tavily,
    IApiKeyStore keys, McpConnector connector, WebPageFetcher web, ILoggerFactory loggers)
{
    /// <summary>The backends of a run with <paramref name="settings"/>.</summary>
    public async Task<IReadOnlyList<IResearchBackend>> ForRunAsync(ResearchSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var backends = new List<IResearchBackend>();
        if (settings.Wikipedia)
        {
            backends.Add(wikipedia);
        }

        if (settings.Wikidata)
        {
            backends.Add(wikidata);
        }

        backends.Add(urls);
        if (settings.Tavily && await HasKeyAsync(cancellationToken).ConfigureAwait(false))
        {
            backends.Add(tavily);
        }

        if (settings.McpEnabled)
        {
            backends.AddRange(settings.McpServers.Where(s => s.Enabled && s.Confirmed && s.SearchTool.Trim().Length > 0).Take(ResearchLimits.McpServersMax)
                .Select(s => new McpResearchBackend(s, connector, web, loggers.CreateLogger<McpResearchBackend>())));
        }

        return backends;
    }

    /// <summary>Whether a Tavily key is saved.</summary>
    public async Task<bool> HasKeyAsync(CancellationToken cancellationToken) =>
        (await keys.ListAsync(TavilyResearchBackend.KeyProvider, cancellationToken).ConfigureAwait(false)).Any(k => k.Secret.Trim().Length > 0);

    /// <summary>Ends the sessions of the run's MCP backends.</summary>
    public static async Task DisposeAsync(IEnumerable<IResearchBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(backends);
        foreach (var backend in backends.OfType<IAsyncDisposable>())
        {
            await backend.DisposeAsync().ConfigureAwait(false);
        }
    }
}
