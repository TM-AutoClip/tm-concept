namespace TMConcept.Core.Models;

/// <summary>Default models of the pipeline steps.</summary>
public interface IStepModelStore
{
    /// <summary>Every saved choice.</summary>
    Task<IReadOnlyList<StepModelChoice>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The choice of a step; <c>null</c> when none (the step's model must be picked).</summary>
    Task<StepModelChoice?> GetAsync(string step, CancellationToken cancellationToken);

    /// <summary>Saves.</summary>
    Task SaveAsync(StepModelChoice choice, CancellationToken cancellationToken);

    /// <summary>Clears a step.</summary>
    Task ClearAsync(string step, CancellationToken cancellationToken);
}
