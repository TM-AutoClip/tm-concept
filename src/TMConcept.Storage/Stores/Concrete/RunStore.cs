using TMConcept.Core.Runs;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IRunStore"/>.</summary>
public sealed class RunStore(ConceptDatabase database) : IRunStore
{
    /// <inheritdoc />
    public async Task AddAsync(Run run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await RunTable.Instance.InsertAsync(db, run, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Run run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await RunTable.Instance.UpsertAsync(db, run, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Run>> ListAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await RunTable.Instance.ListAsync(db, "\"StartedAtUtc\" >= $since", cancellationToken, null, "\"StartedAtUtc\" DESC",
            ("$since", Sql.Ticks(sinceUtc))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<double> AverageConceptOutputTokensAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var recent = await RunTable.Instance.QueryAsync(db,
            RunTable.Instance.Select + " WHERE \"ProfileId\" = $profile AND \"Kind\" = $kind AND \"Status\" = $status ORDER BY \"StartedAtUtc\" DESC LIMIT 20",
            cancellationToken, null, ("$profile", Sql.Id(profileId)), ("$kind", (int)RunKind.Concepts), ("$status", (int)RunStatus.Succeeded))
            .ConfigureAwait(false);
        var items = recent.Sum(r => r.Accepted + r.RejectedDuplicates);
        return items == 0 ? 0 : recent.Sum(r => (double)r.OutputTokens) / items;
    }
}
