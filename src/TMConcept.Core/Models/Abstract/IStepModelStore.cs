namespace TMConcept.Core.Models;

/// <summary>
/// The models of the pipeline steps, kept per profile: the last choice made on the page that uses the step (saved when a process starts),
/// shown again the next time. There are no general defaults (since 0.1.6).
/// </summary>
public interface IStepModelStore
{
    /// <summary>Every saved choice of one profile.</summary>
    Task<IReadOnlyList<StepModelChoice>> ListAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>The saved choice of a step of a profile; <c>null</c> when none was made yet.</summary>
    Task<StepModelChoice?> GetAsync(string step, Guid profileId, CancellationToken cancellationToken);

    /// <summary>Saves (<see cref="StepModelChoice.ProfileId"/> is required).</summary>
    Task SaveAsync(StepModelChoice choice, CancellationToken cancellationToken);

    /// <summary>Forgets a step's choice of a profile.</summary>
    Task ClearAsync(string step, Guid profileId, CancellationToken cancellationToken);
}
