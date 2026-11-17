namespace TMConcept.Core.Research;

/// <summary>
/// One thing a backend found: the backend's id, the title, the address, the language and a short snippet. <see cref="EntityKey"/> groups
/// the versions of one thing (a Wikipedia article's Wikidata item, so one entity in many languages is one source group; the normalized
/// address otherwise, see <see cref="ResearchText.UrlKey"/>). <see cref="Alternates"/>: the same article in other languages.
/// <see cref="InlineText"/>: the text itself when the backend gave it with the hit (an MCP tool), so nothing more is fetched.
/// <see cref="Rank"/>: its place in its backend's answer (0 = first).
/// </summary>
public sealed record ResearchHit(string BackendId, string Title, string Url, string Language)
{
    /// <summary>A short description (shown, never sent to the model).</summary>
    public string Snippet { get; init; } = string.Empty;

    /// <summary>The key one thing's versions share (empty = the address).</summary>
    public string EntityKey { get; init; } = string.Empty;

    /// <summary>The same article in other languages.</summary>
    public IReadOnlyList<ResearchAlternate> Alternates { get; init; } = [];

    /// <summary>The text itself, when the backend gave it (empty = fetch <see cref="Url"/>).</summary>
    public string InlineText { get; init; } = string.Empty;

    /// <summary>Place in its backend's answer.</summary>
    public int Rank { get; init; }
}
