using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Runs;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IRunStore"/>.</summary>
public sealed class RunStore(IDbContextFactory<ConceptDbContext> factory) : IRunStore
{
    /// <inheritdoc />
    public async Task AddAsync(Run run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Run run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Runs.Update(run);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Run>> ListAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var runs = await db.Runs.AsNoTracking().Where(r => r.StartedAtUtc >= sinceUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
        return runs.OrderByDescending(r => r.StartedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task<double> AverageConceptOutputTokensAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var runs = await db.Runs.AsNoTracking()
            .Where(r => r.ProfileId == profileId && r.Kind == RunKind.Concepts && r.Status == RunStatus.Succeeded)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var recent = runs.OrderByDescending(r => r.StartedAtUtc).Take(20).ToList();
        var items = recent.Sum(r => r.Accepted + r.RejectedDuplicates);
        return items == 0 ? 0 : recent.Sum(r => (double)r.OutputTokens) / items;
    }
}
