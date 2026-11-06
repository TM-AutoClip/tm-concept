using TMConcept.Core.Models;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IStepModelStore"/>: general defaults in <c>step_models</c>, profile overrides in <c>profile_step_models</c>.</summary>
public sealed class StepModelStore(ConceptDatabase database) : IStepModelStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StepModelChoice>> ListAsync(Guid? profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return profileId is { } profile
            ? await ProfileStepModelTable.Instance.ListAsync(db, "\"ProfileId\" = $profile", cancellationToken, null, string.Empty, ("$profile", Sql.Id(profile)))
                .ConfigureAwait(false)
            : await StepModelTable.Instance.ListAsync(db, string.Empty, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StepModelChoice?> GetAsync(string step, Guid? profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return profileId is { } profile
            ? await ProfileStepModelTable.Instance.FirstAsync(db, "\"ProfileId\" = $profile AND \"Step\" = $step", cancellationToken, null,
                ("$profile", Sql.Id(profile)), ("$step", step)).ConfigureAwait(false)
            : await StepModelTable.Instance.FirstAsync(db, "\"Step\" = $step", cancellationToken, null, ("$step", step)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(StepModelChoice choice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choice);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (choice.ProfileId is null)
        {
            await StepModelTable.Instance.UpsertAsync(db, choice, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ProfileStepModelTable.Instance.UpsertAsync(db, choice, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(string step, Guid? profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (profileId is { } profile)
        {
            await ProfileStepModelTable.Instance.DeleteAsync(db, "\"ProfileId\" = $profile AND \"Step\" = $step", cancellationToken, null,
                ("$profile", Sql.Id(profile)), ("$step", step)).ConfigureAwait(false);
        }
        else
        {
            await StepModelTable.Instance.DeleteAsync(db, "\"Step\" = $step", cancellationToken, null, ("$step", step)).ConfigureAwait(false);
        }
    }
}
