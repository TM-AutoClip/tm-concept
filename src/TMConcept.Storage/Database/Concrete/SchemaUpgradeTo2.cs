using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace TMConcept.Storage.Database;

/// <summary>
/// Version 1 (0.1.0–0.1.4) → 2 (0.1.5): the profile step choices table comes; the price overrides, the profiles' voice columns and the
/// image and voice step defaults go (not carried over).
/// </summary>
internal sealed class SchemaUpgradeTo2 : SchemaUpgradeBase
{
    /// <inheritdoc />
    public override int From => 1;

    /// <inheritdoc />
    public override async Task ApplyAsync(ConceptDbContext db, IReadOnlySet<string> tables, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(tables);
        var operations = new List<MigrationOperation>();
        if (!tables.Contains(ConceptSchema.ProfileStepModels))
        {
            operations.AddRange(Create(db, ConceptSchema.ProfileStepModels));
        }

        if (tables.Contains(ConceptSchema.PriceOverrides))
        {
            operations.Add(new DropTableOperation { Name = ConceptSchema.PriceOverrides });
        }

        foreach (var column in ConceptSchema.RemovedProfileColumns)
        {
            if (await HasColumnAsync(db, ConceptSchema.Profiles, column, cancellationToken).ConfigureAwait(false))
            {
                operations.Add(new DropColumnOperation { Table = ConceptSchema.Profiles, Name = column });
            }
        }

        await RunAsync(db, operations, cancellationToken).ConfigureAwait(false);
        if (tables.Contains(ConceptSchema.GeneralStepModels))
        {
            var removed = ConceptSchema.RemovedSteps;
            await db.GeneralStepModels.Where(s => removed.Contains(s.Step)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
