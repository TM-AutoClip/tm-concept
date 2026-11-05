using TMConcept.Core.Models;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IPriceOverrideStore"/>.</summary>
public sealed class PriceOverrideStore(ConceptDatabase database) : IPriceOverrideStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelPriceOverride>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await PriceOverrideTable.Instance.ListAsync(db, string.Empty, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ModelPriceOverride price, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(price);
        price.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await PriceOverrideTable.Instance.UpsertAsync(db, price, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string provider, string modelId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await PriceOverrideTable.Instance.DeleteAsync(db, "\"Provider\" = $provider AND \"ModelId\" = $model", cancellationToken, null,
            ("$provider", provider), ("$model", modelId)).ConfigureAwait(false);
    }
}
