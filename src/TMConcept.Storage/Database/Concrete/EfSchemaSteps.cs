using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TMConcept.Storage.Database;

/// <summary>
/// The app's schema steps: the EF Core migrations in <c>Database/Concrete/Migrations</c>, written by <c>tools/database-step.ps1</c> (never by
/// hand, never as SQL). EF Core records each applied step in the file's <c>__EFMigrationsHistory</c> table and applies one step in its own
/// transaction.
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
        return db.GetService<IMigrator>().MigrateAsync(step, cancellationToken);
    }
}
