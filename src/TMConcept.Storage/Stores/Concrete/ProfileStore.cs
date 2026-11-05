using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Profiles;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IProfileStore"/>.</summary>
public sealed class ProfileStore(IDbContextFactory<ConceptDbContext> factory) : IProfileStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Profile>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var all = await db.Profiles.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return all.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public async Task<Profile?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Profile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var exists = await db.Profiles.AnyAsync(p => p.Id == profile.Id, cancellationToken).ConfigureAwait(false);
        profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (exists)
        {
            // The concept counter is owned by the concept store; a stale copy from the editor must not move it back.
            var counter = await db.Profiles.Where(p => p.Id == profile.Id).Select(p => p.ConceptCounter).FirstAsync(cancellationToken).ConfigureAwait(false);
            profile.ConceptCounter = Math.Max(profile.ConceptCounter, counter);
            db.Profiles.Update(profile);
        }
        else
        {
            db.Profiles.Add(profile);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Profiles.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProfileImage>> ImagesAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var all = await db.ProfileImages.AsNoTracking().Where(i => i.ProfileId == profileId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return all.OrderBy(i => i.AddedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task SaveImageAsync(ProfileImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await db.ProfileImages.AnyAsync(i => i.Id == image.Id, cancellationToken).ConfigureAwait(false))
        {
            db.ProfileImages.Update(image);
        }
        else
        {
            db.ProfileImages.Add(image);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteImageAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.ProfileImages.Where(i => i.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
