namespace TMConcept.Core.Models;

/// <summary>The models of the pipeline steps: the general defaults (<c>profileId</c> null) and each profile's overrides.</summary>
public interface IStepModelStore
{
    /// <summary>Every saved choice of the general defaults (<paramref name="profileId"/> null) or of one profile's overrides.</summary>
    Task<IReadOnlyList<StepModelChoice>> ListAsync(Guid? profileId, CancellationToken cancellationToken);

    /// <summary>The saved choice of a step; <c>null</c> when none (general: not chosen; profile: uses the general default).</summary>
    Task<StepModelChoice?> GetAsync(string step, Guid? profileId, CancellationToken cancellationToken);

    /// <summary>Saves (<see cref="StepModelChoice.ProfileId"/> says where).</summary>
    Task SaveAsync(StepModelChoice choice, CancellationToken cancellationToken);

    /// <summary>Clears a step (general: back to not chosen; profile: back to the general default).</summary>
    Task ClearAsync(string step, Guid? profileId, CancellationToken cancellationToken);
}
