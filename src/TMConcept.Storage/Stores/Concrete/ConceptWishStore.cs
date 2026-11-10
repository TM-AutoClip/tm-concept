using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Concepts;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IConceptWishStore"/>: the table <c>concept_wishes</c>.</summary>
public sealed class ConceptWishStore(ConceptDatabase database) : IConceptWishStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ConceptWish>> ListAsync(Guid profileId, int max, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.ConceptWishes.Where(w => w.ProfileId == profileId).OrderByDescending(w => w.CreatedAtUtc).Take(max)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(ConceptWish wish, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wish);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The same wish again is one wish (moved to the front), so a fix asked five times does not push the others out.
        await db.ConceptWishes.Where(w => w.ProfileId == wish.ProfileId && w.Text == wish.Text).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        db.ConceptWishes.Add(wish);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var old = await db.ConceptWishes.Where(w => w.ProfileId == wish.ProfileId).OrderByDescending(w => w.CreatedAtUtc).Skip(PromptMemoryLimits.WishesKept)
            .Select(w => w.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (old.Count > 0)
        {
            await db.ConceptWishes.Where(w => old.Contains(w.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
