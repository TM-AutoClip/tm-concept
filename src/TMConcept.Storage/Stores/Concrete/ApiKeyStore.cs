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
        var list = provider is null
            ? await ApiKeyTable.Instance.ListAsync(db, string.Empty, cancellationToken).ConfigureAwait(false)
            : await ApiKeyTable.Instance.ListAsync(db, "\"Provider\" = $provider", cancellationToken, null, string.Empty, ("$provider", provider))
                .ConfigureAwait(false);
        return list.OrderBy(k => k.Provider, StringComparer.Ordinal).ThenBy(k => k.CreatedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ApiKeyTable.Instance.FirstAsync(db, "\"Id\" = $id", cancellationToken, null, ("$id", Sql.Id(id))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ApiKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ApiKeyTable.Instance.UpsertAsync(db, key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ApiKeyTable.Instance.DeleteAsync(db, "\"Id\" = $id", cancellationToken, null, ("$id", Sql.Id(id))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkOkAsync(Guid id, DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await Sql.ExecuteAsync(db, "UPDATE \"api_keys\" SET \"LastOkAtUtc\" = $at WHERE \"Id\" = $id", cancellationToken, null,
            ("$at", Sql.Ticks(atUtc)), ("$id", Sql.Id(id))).ConfigureAwait(false);
    }
}
