using TMConcept.Core.Concepts;
using TMConcept.Core.Duplicates;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The real concept store, counting how often the new concepts are listed (only the concepts page lists them, so a concepts page that reads
/// again shows here; other pages listing other statuses do not count).
/// </summary>
internal sealed class CountingConceptStore(IConceptStore inner) : IConceptStore
{
    public int NewLists { get; set; }

    public Task<IReadOnlyList<Concept>> ListAsync(Guid profileId, IReadOnlyCollection<ConceptStatus> statuses, bool withContent,
        CancellationToken cancellationToken)
    {
        if (statuses.Contains(ConceptStatus.New))
        {
            NewLists++;
        }

        return inner.ListAsync(profileId, statuses, withContent, cancellationToken);
    }

    public Task<Concept?> GetAsync(Guid id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<DuplicateSubject>> HistoryAsync(Guid profileId, CancellationToken cancellationToken) =>
        inner.HistoryAsync(profileId, cancellationToken);

    public Task AddAsync(IReadOnlyList<Concept> concepts, CancellationToken cancellationToken) => inner.AddAsync(concepts, cancellationToken);

    public Task UpdateContentAsync(Guid id, string content, CancellationToken cancellationToken) =>
        inner.UpdateContentAsync(id, content, cancellationToken);

    public Task UpdateTextAsync(Guid id, string title, string summary, string content, CancellationToken cancellationToken) =>
        inner.UpdateTextAsync(id, title, summary, content, cancellationToken);

    public Task SetStatusAsync(IReadOnlyCollection<Guid> ids, ConceptStatus status, CancellationToken cancellationToken) =>
        inner.SetStatusAsync(ids, status, cancellationToken);

    public Task TrashAsync(IReadOnlyCollection<Guid> ids, TrashReason reason, CancellationToken cancellationToken) =>
        inner.TrashAsync(ids, reason, cancellationToken);

    public Task<int> DeletePermanentlyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        inner.DeletePermanentlyAsync(ids, cancellationToken);

    public Task<int> CountAsync(Guid profileId, ConceptStatus status, CancellationToken cancellationToken) =>
        inner.CountAsync(profileId, status, cancellationToken);
}
