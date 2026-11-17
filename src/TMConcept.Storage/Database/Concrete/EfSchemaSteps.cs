using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TMConcept.Core.Concepts;

namespace TMConcept.Storage.Database;

/// <summary>
/// The app's schema steps: the EF Core migrations in <c>Database/Concrete/Migrations</c>, written by <c>tools/database-step.ps1</c> (never by
/// hand, never as SQL). EF Core records each applied step in the file's <c>__EFMigrationsHistory</c> table and applies one step in its own
/// transaction. A step that must give the rows that were already there a value other than its new column's default has a data step
/// (<see cref="AfterStepAsync"/>, EF Core LINQ, no SQL): it runs right after its step, on the same working copy
/// (<see cref="SchemaUpgrader"/>), so a failure leaves the file as it was.
/// </summary>
internal sealed class EfSchemaSteps : ISchemaSteps
{
    /// <summary>The one instance.</summary>
    public static EfSchemaSteps Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<string> Known(ConceptDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.GetMigrations().ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> AppliedAsync(ConceptDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
    }

    /// <inheritdoc />
    public Task ApplyAsync(ConceptDbContext db, string step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return ApplyStepAsync(db, step, cancellationToken);
    }

    private static async Task ApplyStepAsync(ConceptDbContext db, string step, CancellationToken cancellationToken)
    {
        await db.GetService<IMigrator>().MigrateAsync(step, cancellationToken).ConfigureAwait(false);
        await AfterStepAsync(db, SchemaUpgrader.Name(step), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The data part of a step, for the rows that existed before it (none in a new file).
    /// <list type="bullet">
    /// <item>Version0118: 0.1.17 blocked every topic suggestion for good and never recorded which ones got a concept; they keep that meaning
    /// (<see cref="TopicOutcome.Picked"/>), or a topic that already has a concept would come back as "recently shown". Only the suggestions
    /// made from 0.1.18 on may come again.</item>
    /// </list>
    /// </summary>
    private static async Task AfterStepAsync(ConceptDbContext db, string name, CancellationToken cancellationToken)
    {
        if (name == "Version0118")
        {
            await db.TopicSuggestions.ExecuteUpdateAsync(u => u.SetProperty(t => t.Outcome, TopicOutcome.Picked), cancellationToken).ConfigureAwait(false);
        }
    }
}
