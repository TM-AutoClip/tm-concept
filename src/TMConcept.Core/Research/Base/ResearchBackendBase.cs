using System.Text;
using TMConcept.Core.Documents;

namespace TMConcept.Core.Research;

/// <summary>
/// The common part of a research backend: an empty query asks nothing, the hits are cut to the asked number and carry the backend's id and
/// their rank, a hit of another backend is refused, and every fetched text (a Markdown or plain-text copy) is cleaned of the characters that
/// hide or reorder text before it is stored (<see cref="ResearchText.Clean"/>).
/// </summary>
public abstract class ResearchBackendBase : IResearchBackend
{
    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract ResearchBackendKind Kind { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public virtual bool NeedsKey => false;

    /// <inheritdoc />
    public virtual bool PerLanguage => false;

    /// <summary>Whether the backend answers a query without text (the addresses backend reads only the addresses).</summary>
    protected virtual bool NeedsText => true;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResearchHit>> SearchAsync(ResearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if ((NeedsText && query.Text.Trim().Length == 0) || query.MaxHits <= 0)
        {
            return [];
        }

        var hits = await SearchCoreAsync(query with { Text = query.Text.Trim() }, cancellationToken).ConfigureAwait(false);
        return hits.Where(h => h.Title.Trim().Length > 0 && (h.Url.Length > 0 || h.InlineText.Length > 0))
            .Take(query.MaxHits)
            .Select((h, i) => h with { BackendId = Id, Rank = i, Title = TextOneLine(h.Title), Snippet = TextOneLine(h.Snippet) })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<FetchedDocument> FetchAsync(ResearchHit hit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (!string.Equals(hit.BackendId, Id, StringComparison.Ordinal))
        {
            throw new ArgumentException($"The {Id} backend got a hit of {hit.BackendId}.", nameof(hit));
        }

        var fetched = await FetchCoreAsync(hit, cancellationToken).ConfigureAwait(false);
        return fetched.Extension is ".md" or ".txt"
            ? fetched with { Content = Encoding.UTF8.GetBytes(ResearchText.Clean(Encoding.UTF8.GetString(fetched.Content))), Title = TextOneLine(fetched.Title) }
            : fetched with { Title = TextOneLine(fetched.Title) };
    }

    /// <summary>The backend's own search.</summary>
    protected abstract Task<IReadOnlyList<ResearchHit>> SearchCoreAsync(ResearchQuery query, CancellationToken cancellationToken);

    /// <summary>The backend's own fetch.</summary>
    protected abstract Task<FetchedDocument> FetchCoreAsync(ResearchHit hit, CancellationToken cancellationToken);

    private static string TextOneLine(string text) =>
        Text.TextClean.OneLine(ResearchText.Clean(text), DocumentLimits.TitleMax);
}
