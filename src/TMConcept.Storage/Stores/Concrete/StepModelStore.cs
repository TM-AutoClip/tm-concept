using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Models;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IStepModelStore"/>: each profile's choices in <c>profile_step_models</c> (removed with the profile).</summary>
public sealed class StepModelStore(ConceptDatabase database) : IStepModelStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StepModelChoice>> ListAsync(Guid profileId, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<IReadOnlyList<StepModelChoice>>(async db =>
        {
            return await db.ProfileStepModels.Where(s => s.ProfileId == profileId).ToListAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StepModelChoice?> GetAsync(string step, Guid profileId, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<StepModelChoice?>(async db =>
        {
            return await db.ProfileStepModels.FirstOrDefaultAsync(s => s.ProfileId == profileId && s.Step == step, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(StepModelChoice choice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.ProfileId is not { } profileId)
        {
            throw new ArgumentException("A step choice belongs to a profile.", nameof(choice));
        }

        await database.WriteAsync(async db =>
        {
            var step = choice.Step;
            await db.UpsertAsync(choice, s => s.ProfileId == profileId && s.Step == step, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ClearAsync(string step, Guid profileId, CancellationToken cancellationToken)
    {
        await database.WriteAsync(async db =>
        {
            await db.ProfileStepModels.Where(s => s.ProfileId == profileId && s.Step == step).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
