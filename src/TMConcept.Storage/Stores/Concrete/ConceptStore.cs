using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Concepts;
using TMConcept.Core.Duplicates;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IConceptStore"/>.</summary>
public sealed class ConceptStore(ConceptDatabase database) : IConceptStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Concept>> ListAsync(Guid profileId, IReadOnlyCollection<ConceptStatus> statuses, bool withContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var query = db.Concepts.Where(c => c.ProfileId == profileId && statuses.Contains(c.Status)).OrderByDescending(c => c.Number);

        // Without content the long text is not even read (lists show title and summary).
        return withContent
            ? await query.ToListAsync(cancellationToken).ConfigureAwait(false)
            : await query.Select(c => new Concept
            {
                Id = c.Id,
                ProfileId = c.ProfileId,
                Number = c.Number,
                Status = c.Status,
                Title = c.Title,
                Summary = c.Summary,
                Content = string.Empty,
                Language = c.Language,
                RunId = c.RunId,
                SimilarToId = c.SimilarToId,
                Similarity = c.Similarity,
                DuplicateNote = c.DuplicateNote,
                CreatedAtUtc = c.CreatedAtUtc,
                DecidedAtUtc = c.DecidedAtUtc,
                TrashReason = c.TrashReason,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Concept?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Concepts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DuplicateSubject>> HistoryAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Concepts.Where(c => c.ProfileId == profileId).OrderBy(c => c.Number)
            .Select(c => new DuplicateSubject(c.Id, c.Number, c.Title, c.Summary)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(IReadOnlyList<Concept> concepts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(concepts);
        if (concepts.Count == 0)
        {
            return;
        }

        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var group in concepts.GroupBy(c => c.ProfileId))
        {
            var profileId = group.Key;
            var counter = await db.Profiles.Where(p => p.Id == profileId).Select(p => (int?)p.ConceptCounter).FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidOperationException("The profile of the concepts does not exist.");
            foreach (var concept in group)
            {
                counter++;
                concept.Number = counter;
                concept.Content = concept.Content.Replace("\0", string.Empty, StringComparison.Ordinal);
                db.Concepts.Add(concept);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await db.Profiles.Where(p => p.Id == profileId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ConceptCounter, counter), cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateContentAsync(Guid id, string content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var text = content.Replace("\0", string.Empty, StringComparison.Ordinal);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.Concepts.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Content, text), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateTextAsync(Guid id, string title, string summary, string content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(content);
        var newTitle = title.Replace("\0", string.Empty, StringComparison.Ordinal);
        var newSummary = summary.Replace("\0", string.Empty, StringComparison.Ordinal);
        var text = content.Replace("\0", string.Empty, StringComparison.Ordinal);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.Concepts.Where(c => c.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Title, newTitle)
            .SetProperty(c => c.Summary, newSummary)
            .SetProperty(c => c.Content, text), cancellationToken).ConfigureAwait(false);

        // The vectors were made from the old title and summary: a stale one would compare the new concept as the old one.
        await db.Embeddings.Where(e => e.ConceptId == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetStatusAsync(IReadOnlyCollection<Guid> ids, ConceptStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? now = DateTimeOffset.UtcNow;

        // Approved or trashed: decided now; back to new: undecided; any other status keeps its decision time.
        await db.Concepts.Where(c => ids.Contains(c.Id)).ExecuteUpdateAsync(s =>
        {
            s.SetProperty(c => c.Status, status);
            s.SetProperty(c => c.TrashReason, TrashReason.None);
            if (status is ConceptStatus.Approved or ConceptStatus.Trashed)
            {
                s.SetProperty(c => c.DecidedAtUtc, now);
            }
            else if (status == ConceptStatus.New)
            {
                s.SetProperty(c => c.DecidedAtUtc, (DateTimeOffset?)null);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task TrashAsync(IReadOnlyCollection<Guid> ids, TrashReason reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? now = DateTimeOffset.UtcNow;
        await db.Concepts.Where(c => ids.Contains(c.Id)).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Status, ConceptStatus.Trashed)
            .SetProperty(c => c.DecidedAtUtc, now)
            .SetProperty(c => c.TrashReason, reason), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeletePermanentlyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Secure delete (SQLite3 Multiple Ciphers is built with it; checked whenever a file comes in) overwrites the deleted rows and the
        // pages they free with zeros (DeletedDataTests). The file is not compacted: VACUUM is SQL with no EF Core counterpart (open decision,
        // release notes 0.1.7).
        return await db.Concepts.Where(c => ids.Contains(c.Id) && c.Status == ConceptStatus.Trashed).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(Guid profileId, ConceptStatus status, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Concepts.CountAsync(c => c.ProfileId == profileId && c.Status == status, cancellationToken).ConfigureAwait(false);
    }
}
