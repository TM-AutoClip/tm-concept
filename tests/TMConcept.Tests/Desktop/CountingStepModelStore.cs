using TMConcept.Core.Models;

namespace TMConcept.Tests.Desktop;

/// <summary>The real step model store, counting how often the choices of a profile are read (a page that reads again shows here).</summary>
internal sealed class CountingStepModelStore(IStepModelStore inner) : IStepModelStore
{
    public int Lists { get; set; }

    public Task<IReadOnlyList<StepModelChoice>> ListAsync(Guid profileId, CancellationToken cancellationToken)
    {
        Lists++;
        return inner.ListAsync(profileId, cancellationToken);
    }

    public Task<StepModelChoice?> GetAsync(string step, Guid profileId, CancellationToken cancellationToken) =>
        inner.GetAsync(step, profileId, cancellationToken);

    public Task SaveAsync(StepModelChoice choice, CancellationToken cancellationToken) => inner.SaveAsync(choice, cancellationToken);

    public Task ClearAsync(string step, Guid profileId, CancellationToken cancellationToken) => inner.ClearAsync(step, profileId, cancellationToken);
}
