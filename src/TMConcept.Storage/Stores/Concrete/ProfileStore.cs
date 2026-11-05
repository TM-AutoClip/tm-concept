using Microsoft.Data.Sqlite;
using TMConcept.Core.Profiles;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IProfileStore"/>.</summary>
public sealed class ProfileStore(ConceptDatabase database) : IProfileStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Profile>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var all = await ProfileTable.Instance.ListAsync(db, string.Empty, cancellationToken).ConfigureAwait(false);
        return all.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public async Task<Profile?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ProfileTable.Instance.FirstAsync(db, "\"Id\" = $id", cancellationToken, null, ("$id", Sql.Id(id))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Profile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The concept counter is owned by the concept store; a stale copy from the editor must not move it back.
        var counter = await Sql.ScalarAsync(db, "SELECT \"ConceptCounter\" FROM \"profiles\" WHERE \"Id\" = $id", cancellationToken, transaction,
            ("$id", Sql.Id(profile.Id))).ConfigureAwait(false);
        profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (counter is { } stored)
        {
            profile.ConceptCounter = Math.Max(profile.ConceptCounter, (int)stored);
        }

        await ProfileTable.Instance.UpsertAsync(db, profile, cancellationToken, transaction).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ProfileTable.Instance.DeleteAsync(db, "\"Id\" = $id", cancellationToken, null, ("$id", Sql.Id(id))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProfileImage>> ImagesAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ProfileImageTable.Instance.ListAsync(db, "\"ProfileId\" = $profile", cancellationToken, null, "\"AddedAtUtc\"",
            ("$profile", Sql.Id(profileId))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveImageAsync(ProfileImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ProfileImageTable.Instance.UpsertAsync(db, image, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteImageAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ProfileImageTable.Instance.DeleteAsync(db, "\"Id\" = $id", cancellationToken, null, ("$id", Sql.Id(id))).ConfigureAwait(false);
    }
}
