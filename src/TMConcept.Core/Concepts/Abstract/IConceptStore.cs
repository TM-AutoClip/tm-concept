using TMConcept.Core.Duplicates;

namespace TMConcept.Core.Concepts;

/// <summary>Concepts of the profiles.</summary>
public interface IConceptStore
{
    /// <summary>
    /// Concepts of a profile with the given statuses, newest first. Without <paramref name="withContent"/> the content is left empty (lists
    /// show only title and summary; the full text is read when a card is opened).
    /// </summary>
    Task<IReadOnlyList<Concept>> ListAsync(Guid profileId, IReadOnlyCollection<ConceptStatus> statuses, bool withContent, CancellationToken cancellationToken);

    /// <summary>One concept with its content.</summary>
    Task<Concept?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Number, title and summary of every concept of a profile (every status): the duplicate check's history.</summary>
    Task<IReadOnlyList<DuplicateSubject>> HistoryAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Adds new concepts; each gets the next number of its profile (numbers are never reused).</summary>
    Task AddAsync(IReadOnlyList<Concept> concepts, CancellationToken cancellationToken);

    /// <summary>Replaces the text of a concept (edited by the user: a fix, placeholders); title, summary and status stay.</summary>
    Task UpdateContentAsync(Guid id, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces title, summary and text of a concept (an AI edit saved by the user); status and number stay. The concept's stored vectors of
    /// the meaning check are deleted with it (they were made from the old title and summary; the next meaning check embeds it again).
    /// </summary>
    Task UpdateTextAsync(Guid id, string title, string summary, string content, CancellationToken cancellationToken);

    /// <summary>Changes the status (approve, reject, restore, queue, produced); the trash reason is cleared (a reason is given only by <see cref="TrashAsync"/>).</summary>
    Task SetStatusAsync(IReadOnlyCollection<Guid> ids, ConceptStatus status, CancellationToken cancellationToken);

    /// <summary>Moves concepts to the trash with a reason (🗑 on a card: topic, telling or none).</summary>
    Task TrashAsync(IReadOnlyCollection<Guid> ids, TrashReason reason, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes trashed concepts for good (a real DELETE; their embeddings go with them, videos keep their record). Concepts that are not in
    /// the trash are left alone. Returns how many were deleted.
    /// </summary>
    Task<int> DeletePermanentlyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Number of concepts of a profile with a status.</summary>
    Task<int> CountAsync(Guid profileId, ConceptStatus status, CancellationToken cancellationToken);
}
