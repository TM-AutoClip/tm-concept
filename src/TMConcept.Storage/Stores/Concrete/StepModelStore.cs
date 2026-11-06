using TMConcept.Core.Models;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IStepModelStore"/>: each profile's choices in <c>profile_step_models</c> (removed with the profile).</summary>
public sealed class StepModelStore(ConceptDatabase database) : IStepModelStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StepModelChoice>> ListAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ProfileStepModelTable.Instance.ListAsync(db, "\"ProfileId\" = $profile", cancellationToken, null, string.Empty, ("$profile", Sql.Id(profileId)))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StepModelChoice?> GetAsync(string step, Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ProfileStepModelTable.Instance.FirstAsync(db, "\"ProfileId\" = $profile AND \"Step\" = $step", cancellationToken, null,
            ("$profile", Sql.Id(profileId)), ("$step", step)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(StepModelChoice choice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.ProfileId is null)
        {
            throw new ArgumentException("A step choice belongs to a profile.", nameof(choice));
        }

        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ProfileStepModelTable.Instance.UpsertAsync(db, choice, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ClearAsync(string step, Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ProfileStepModelTable.Instance.DeleteAsync(db, "\"ProfileId\" = $profile AND \"Step\" = $step", cancellationToken, null,
            ("$profile", Sql.Id(profileId)), ("$step", step)).ConfigureAwait(false);
    }
}
