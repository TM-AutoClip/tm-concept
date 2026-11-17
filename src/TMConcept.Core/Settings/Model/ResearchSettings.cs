using TMConcept.Core.Research;

namespace TMConcept.Core.Settings;

/// <summary>
/// "Araştırmadan" (0.1.22, Settings → Research): which backends a run asks, how many pages it fetches, which extra languages it reads,
/// whether the fetched pages stay in the profile's documents, and the user's MCP servers (advanced, off until switched on). Keys of the keyed
/// backends are API keys like the others (encrypted, never here).
/// </summary>
public sealed class ResearchSettings
{
    /// <summary>Wikipedia is asked (no key).</summary>
    public bool Wikipedia { get; set; } = true;

    /// <summary>Wikidata is asked (no key).</summary>
    public bool Wikidata { get; set; } = true;

    /// <summary>Tavily is asked (only with a saved key).</summary>
    public bool Tavily { get; set; } = true;

    /// <summary>Pages a run fetches at most.</summary>
    public int Pages { get; set; } = ResearchLimits.PagesDefault;

    /// <summary>Extra languages read besides the profile's and English, as typed ("fr, de").</summary>
    public string ExtraLanguages { get; set; } = string.Empty;

    /// <summary>The fetched pages stay in the profile's documents after the run (else the ones the run added go to the recycle bin).</summary>
    public bool KeepInLibrary { get; set; } = true;

    /// <summary>The user's MCP servers are used at all (advanced; off by default).</summary>
    public bool McpEnabled { get; set; }

    /// <summary>The user's MCP servers.</summary>
    public List<McpServerSettings> McpServers { get; set; } = [];
}
