using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Keys;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IApiKeyStore"/>.</summary>
public sealed class ApiKeyStore(ConceptDatabase database) : IApiKeyStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKey>> ListAsync(string? provider, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<IReadOnlyList<ApiKey>>(async db =>
        {
            var list = await (provider is null ? db.ApiKeys : db.ApiKeys.Where(k => k.Provider == provider)).ToListAsync(cancellationToken).ConfigureAwait(false);
            return list.OrderBy(k => k.Provider, StringComparer.Ordinal).ThenBy(k => k.CreatedAtUtc).ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<ApiKey?>(async db =>
        {
            return await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ApiKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        await database.WriteAsync(async db =>
        {
            await db.UpsertAsync(key, k => k.Id == key.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await database.WriteAsync(async db =>
        {
            await db.ApiKeys.Where(k => k.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkOkAsync(Guid id, DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        await database.WriteAsync(async db =>
        {
            await db.ApiKeys.Where(k => k.Id == id).ExecuteUpdateAsync(s => s.SetProperty(k => k.LastOkAtUtc, atUtc), cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
