using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Models;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IPriceOverrideStore"/>.</summary>
public sealed class PriceOverrideStore(IDbContextFactory<ConceptDbContext> factory) : IPriceOverrideStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelPriceOverride>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.PriceOverrides.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ModelPriceOverride price, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(price);
        price.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await db.PriceOverrides.AnyAsync(p => p.Provider == price.Provider && p.ModelId == price.ModelId, cancellationToken).ConfigureAwait(false))
        {
            db.PriceOverrides.Update(price);
        }
        else
        {
            db.PriceOverrides.Add(price);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string provider, string modelId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.PriceOverrides.Where(p => p.Provider == provider && p.ModelId == modelId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
