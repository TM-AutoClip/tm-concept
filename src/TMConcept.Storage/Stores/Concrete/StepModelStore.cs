using TMConcept.Core.Models;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IStepModelStore"/>.</summary>
public sealed class StepModelStore(ConceptDatabase database) : IStepModelStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StepModelChoice>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await StepModelTable.Instance.ListAsync(db, string.Empty, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StepModelChoice?> GetAsync(string step, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await StepModelTable.Instance.FirstAsync(db, "\"Step\" = $step", cancellationToken, null, ("$step", step)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(StepModelChoice choice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choice);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await StepModelTable.Instance.UpsertAsync(db, choice, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ClearAsync(string step, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await StepModelTable.Instance.DeleteAsync(db, "\"Step\" = $step", cancellationToken, null, ("$step", step)).ConfigureAwait(false);
    }
}
