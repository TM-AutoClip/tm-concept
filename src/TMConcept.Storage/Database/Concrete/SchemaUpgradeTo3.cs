using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TMConcept.Core.Models;

namespace TMConcept.Storage.Database;

/// <summary>
/// Version 2 (0.1.5) → 3 (0.1.6): the general step defaults become each profile's own choice where the profile had none (an explicit choice
/// of a profile, "off" included, stays), then <c>step_models</c> goes.
/// </summary>
internal sealed class SchemaUpgradeTo3 : SchemaUpgradeBase
{
    /// <inheritdoc />
    public override int From => 2;

    /// <inheritdoc />
    public override async Task ApplyAsync(ConceptDbContext db, IReadOnlySet<string> tables, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(tables);
        if (!tables.Contains(ConceptSchema.GeneralStepModels))
        {
            return;
        }

        var generals = await db.GeneralStepModels.ToListAsync(cancellationToken).ConfigureAwait(false);
        var profiles = await db.Profiles.Select(p => p.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var chosen = (await db.ProfileStepModels.Select(c => new { c.ProfileId, c.Step }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(c => (c.ProfileId, c.Step)).ToHashSet();
        foreach (var profile in profiles)
        {
            foreach (var general in generals.Where(g => !chosen.Contains(((Guid?)profile, g.Step))))
            {
                db.ProfileStepModels.Add(new StepModelChoice
                {
                    ProfileId = profile,
                    Step = general.Step,
                    Provider = general.Provider,
                    Model = general.Model,
                    KeyId = general.KeyId,
                    OptionsJson = general.OptionsJson,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RunAsync(db, [new DropTableOperation { Name = ConceptSchema.GeneralStepModels }], cancellationToken).ConfigureAwait(false);
    }
}
