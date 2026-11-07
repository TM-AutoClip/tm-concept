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
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var list = await (provider is null ? db.ApiKeys : db.ApiKeys.Where(k => k.Provider == provider)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return list.OrderBy(k => k.Provider, StringComparer.Ordinal).ThenBy(k => k.CreatedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ApiKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.UpsertAsync(key, k => k.Id == key.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.ApiKeys.Where(k => k.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkOkAsync(Guid id, DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.ApiKeys.Where(k => k.Id == id).ExecuteUpdateAsync(s => s.SetProperty(k => k.LastOkAtUtc, atUtc), cancellationToken).ConfigureAwait(false);
    }
}
