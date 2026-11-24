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
        return await database.ReadAsync<IReadOnlyList<Profile>>(async db =>
        {
            var all = await db.Profiles.ToListAsync(cancellationToken).ConfigureAwait(false);
            return all.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Profile?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<Profile?>(async db =>
        {
            return await db.Profiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Profile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await database.WriteAsync(async db =>
        {
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
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await database.WriteAsync(async db =>
        {
            await db.Profiles.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
