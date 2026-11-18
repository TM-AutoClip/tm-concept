namespace TMConcept.Core.Runs;

/// <summary>Model call groups with their tokens and costs.</summary>
public interface IRunStore
{
    /// <summary>Adds.</summary>
    Task AddAsync(Run run, CancellationToken cancellationToken);

    /// <summary>One run; <c>null</c> when there is none.</summary>
    Task<Run?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Updates (finished, failed).</summary>
    Task UpdateAsync(Run run, CancellationToken cancellationToken);

    /// <summary>Runs started at or after <paramref name="sinceUtc"/>, newest first.</summary>
    Task<IReadOnlyList<Run>> ListAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken);

    /// <summary>
    /// The AI edits of a concept (<see cref="RunKind.ConceptEdit"/> runs that name it, every outcome) that no video's cost lines name yet,
    /// oldest first: what the next video made from the concept records as its edit cost.
    /// </summary>
    Task<IReadOnlyList<Run>> ConceptEditsNotInVideosAsync(Guid conceptId, CancellationToken cancellationToken);

    /// <summary>Average output tokens per concept of a profile's finished concept runs (0 when none): improves the next estimate.</summary>
    Task<double> AverageConceptOutputTokensAsync(Guid profileId, CancellationToken cancellationToken);
}
