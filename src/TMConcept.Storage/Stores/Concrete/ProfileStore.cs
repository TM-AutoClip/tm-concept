using Microsoft.EntityFrameworkCore;
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
        var all = await db.Profiles.ToListAsync(cancellationToken).ConfigureAwait(false);
        return all.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public async Task<Profile?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Profiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Profile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The concept counter is owned by the concept store; a stale copy from the editor must not move it back.
        var id = profile.Id;
        var counter = await db.Profiles.Where(p => p.Id == id).Select(p => (int?)p.ConceptCounter).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (counter is { } stored)
        {
            profile.ConceptCounter = Math.Max(profile.ConceptCounter, stored);
            db.Profiles.Update(profile);
        }
        else
        {
            db.Profiles.Add(profile);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.Profiles.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProfileImage>> ImagesAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.ProfileImages.Where(i => i.ProfileId == profileId).OrderBy(i => i.AddedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveImageAsync(ProfileImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.UpsertAsync(image, i => i.Id == image.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteImageAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.ProfileImages.Where(i => i.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
