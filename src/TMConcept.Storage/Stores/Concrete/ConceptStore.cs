using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Concepts;
using TMConcept.Core.Duplicates;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IConceptStore"/>.</summary>
public sealed class ConceptStore(IDbContextFactory<ConceptDbContext> factory) : IConceptStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Concept>> ListAsync(Guid profileId, IReadOnlyCollection<ConceptStatus> statuses, bool withContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.Concepts.AsNoTracking().Where(c => c.ProfileId == profileId && statuses.Contains(c.Status));
        var list = withContent
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
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return list.OrderByDescending(c => c.Number).ToList();
    }

    /// <inheritdoc />
    public async Task<Concept?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Concepts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DuplicateSubject>> HistoryAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Concepts.AsNoTracking().Where(c => c.ProfileId == profileId)
            .Select(c => new { c.Id, c.Number, c.Title, c.Summary })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.OrderBy(r => r.Number).Select(r => new DuplicateSubject(r.Id, r.Number, r.Title, r.Summary)).ToList();
    }

    /// <inheritdoc />
    public async Task AddAsync(IReadOnlyList<Concept> concepts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(concepts);
        if (concepts.Count == 0)
        {
            return;
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var group in concepts.GroupBy(c => c.ProfileId))
        {
            var profile = await db.Profiles.FirstOrDefaultAsync(p => p.Id == group.Key, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The profile of the concepts does not exist.");
            foreach (var concept in group)
            {
                profile.ConceptCounter++;
                concept.Number = profile.ConceptCounter;
                concept.Content = concept.Content.Replace("\0", string.Empty, StringComparison.Ordinal);
                db.Concepts.Add(concept);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetStatusAsync(IReadOnlyCollection<Guid> ids, ConceptStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var concepts = await db.Concepts.Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        foreach (var concept in concepts)
        {
            concept.Status = status;
            concept.DecidedAtUtc = status is ConceptStatus.Approved or ConceptStatus.Trashed ? now : status == ConceptStatus.New ? null : concept.DecidedAtUtc;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeletePermanentlyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await db.Concepts.Where(c => ids.Contains(c.Id) && c.Status == ConceptStatus.Trashed)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (deleted > 0)
        {
            // Compact the file so freed pages do not linger (secure_delete already zeroed them).
            await db.Database.ExecuteSqlRawAsync("VACUUM;", cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(Guid profileId, ConceptStatus status, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Concepts.CountAsync(c => c.ProfileId == profileId && c.Status == status, cancellationToken).ConfigureAwait(false);
    }
}
