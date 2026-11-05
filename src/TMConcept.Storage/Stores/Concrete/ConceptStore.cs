using Microsoft.Data.Sqlite;
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
        var (clause, parameters) = Sql.In("Status", statuses.Select(s => (object?)(int)s));

        // Without content the long text is not even read (lists show title and summary).
        var select = withContent ? ConceptTable.Instance.Select : ConceptTable.Instance.SelectWithoutContent;
        return await ConceptTable.Instance.QueryAsync(db, select + $" WHERE \"ProfileId\" = $profile AND {clause} ORDER BY \"Number\" DESC",
            cancellationToken, null, [("$profile", Sql.Id(profileId)), .. parameters]).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Concept?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ConceptTable.Instance.FirstAsync(db, "\"Id\" = $id", cancellationToken, null, ("$id", Sql.Id(id))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DuplicateSubject>> HistoryAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Sql.Command(db, "SELECT \"Id\", \"Number\", \"Title\", \"Summary\" FROM \"concepts\" WHERE \"ProfileId\" = $profile ORDER BY \"Number\"",
            null, ("$profile", Sql.Id(profileId)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<DuplicateSubject>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new DuplicateSubject(Sql.ReadId(reader, 0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        }

        return list;
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
        await using var transaction = (SqliteTransaction)await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var group in concepts.GroupBy(c => c.ProfileId))
        {
            var profile = ("$profile", (object?)Sql.Id(group.Key));
            var counter = await Sql.ScalarAsync(db, "SELECT \"ConceptCounter\" FROM \"profiles\" WHERE \"Id\" = $profile", cancellationToken, transaction, profile)
                .ConfigureAwait(false) ?? throw new InvalidOperationException("The profile of the concepts does not exist.");
            foreach (var concept in group)
            {
                counter++;
                concept.Number = (int)counter;
                concept.Content = concept.Content.Replace("\0", string.Empty, StringComparison.Ordinal);
                await ConceptTable.Instance.InsertAsync(db, concept, cancellationToken, transaction).ConfigureAwait(false);
            }

            await Sql.ExecuteAsync(db, "UPDATE \"profiles\" SET \"ConceptCounter\" = $counter WHERE \"Id\" = $profile", cancellationToken, transaction,
                ("$counter", counter), profile).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetStatusAsync(IReadOnlyCollection<Guid> ids, ConceptStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var (clause, parameters) = Sql.In("Id", ids.Select(i => (object?)Sql.Id(i)));

        // Approved or trashed: decided now; back to new: undecided; any other status keeps its decision time.
        var decided = status is ConceptStatus.Approved or ConceptStatus.Trashed ? ", \"DecidedAtUtc\" = $now"
            : status == ConceptStatus.New ? ", \"DecidedAtUtc\" = NULL"
            : string.Empty;
        await Sql.ExecuteAsync(db, $"UPDATE \"concepts\" SET \"Status\" = $status{decided} WHERE {clause}", cancellationToken, null,
            [("$status", (int)status), ("$now", Sql.Ticks(DateTimeOffset.UtcNow)), .. parameters]).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeletePermanentlyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var (clause, parameters) = Sql.In("Id", ids.Select(i => (object?)Sql.Id(i)));
        var deleted = await ConceptTable.Instance.DeleteAsync(db, $"{clause} AND \"Status\" = $trashed", cancellationToken, null,
            [("$trashed", (int)ConceptStatus.Trashed), .. parameters]).ConfigureAwait(false);
        if (deleted > 0)
        {
            // Compact the file so freed pages do not linger (secure_delete already zeroed them).
            await Sql.ExecuteAsync(db, "VACUUM;", cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(Guid profileId, ConceptStatus status, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ConceptTable.Instance.CountAsync(db, "\"ProfileId\" = $profile AND \"Status\" = $status", cancellationToken,
            ("$profile", Sql.Id(profileId)), ("$status", (int)status)).ConfigureAwait(false);
    }
}
