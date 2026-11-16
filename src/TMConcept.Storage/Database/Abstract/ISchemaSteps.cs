namespace TMConcept.Storage.Database;

/// <summary>
/// The schema steps a database file goes through, oldest first: one step for each app version that changed the tables, and each step knows
/// only the schema of the step before it (<see cref="SchemaUpgrader"/> runs them in order, also several after a long pause). The app's
/// steps are EF Core migrations written by tooling (<see cref="EfSchemaSteps"/>); tests give their own.
/// </summary>
internal interface ISchemaSteps
{
    /// <summary>Every step this app knows, oldest first (ids sort in that order).</summary>
    IReadOnlyList<string> Known(ConceptDbContext db);

    /// <summary>The steps the open file records as applied, oldest first (empty for a file without a record).</summary>
    Task<IReadOnlyList<string>> AppliedAsync(ConceptDbContext db, CancellationToken cancellationToken);

    /// <summary>Applies one step to the open file and records it (all of it or nothing).</summary>
    Task ApplyAsync(ConceptDbContext db, string step, CancellationToken cancellationToken);
}
