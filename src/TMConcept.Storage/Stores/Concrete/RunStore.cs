using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Runs;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IRunStore"/>.</summary>
public sealed class RunStore(ConceptDatabase database) : IRunStore
{
    // The recent concept runs the average output per concept is taken from.
    private const int AverageRuns = 20;

    /// <inheritdoc />
    public async Task AddAsync(Run run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Run?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Runs.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Run run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.UpsertAsync(run, r => r.Id == run.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Run>> ListAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Runs.Where(r => r.StartedAtUtc >= sinceUtc).OrderByDescending(r => r.StartedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<double> AverageConceptOutputTokensAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var recent = await db.Runs.Where(r => r.ProfileId == profileId && r.Kind == RunKind.Concepts && r.Status == RunStatus.Succeeded)
            .OrderByDescending(r => r.StartedAtUtc).Take(AverageRuns).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = recent.Sum(r => r.Accepted + r.RejectedDuplicates);
        return items == 0 ? 0 : recent.Sum(r => (double)r.OutputTokens) / items;
    }
}
