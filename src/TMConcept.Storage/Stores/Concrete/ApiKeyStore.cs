using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Keys;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IApiKeyStore"/>.</summary>
public sealed class ApiKeyStore(IDbContextFactory<ConceptDbContext> factory) : IApiKeyStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKey>> ListAsync(string? provider, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.ApiKeys.AsNoTracking();
        if (provider is not null)
        {
            query = query.Where(k => k.Provider == provider);
        }

        var list = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return list.OrderBy(k => k.Provider, StringComparer.Ordinal).ThenBy(k => k.CreatedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ApiKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await db.ApiKeys.AnyAsync(k => k.Id == key.Id, cancellationToken).ConfigureAwait(false))
        {
            db.ApiKeys.Update(key);
        }
        else
        {
            db.ApiKeys.Add(key);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.ApiKeys.Where(k => k.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkOkAsync(Guid id, DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken).ConfigureAwait(false);
        if (key is not null)
        {
            key.LastOkAtUtc = atUtc;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
