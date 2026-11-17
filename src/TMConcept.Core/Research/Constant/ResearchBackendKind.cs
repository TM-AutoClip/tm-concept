namespace TMConcept.Core.Research;

/// <summary>What a research backend of "Araştırmadan" (0.1.22) is. The app calls every backend itself in a fixed plan (search, then fetch); no model ever drives one.</summary>
public enum ResearchBackendKind
{
    /// <summary>Wikipedia in any language: search, then the article's plain text (no key; on by default).</summary>
    Wikipedia = 0,

    /// <summary>Wikidata: items and their facts with labels in the profile's language (no key; on by default).</summary>
    Wikidata = 1,

    /// <summary>The addresses the user gave (a web page or a Wikipedia article each; no search).</summary>
    Url = 2,

    /// <summary>Tavily web search with the user's own key (off until a key is added).</summary>
    Tavily = 3,

    /// <summary>A search or fetch tool of an MCP server the user added (advanced; off by default).</summary>
    Mcp = 4,
}
