using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TMConcept.Storage.Database;

namespace TMConcept.Tests.Storage;

/// <summary>
/// Schema steps for the chain tests, all through EF Core: the first step creates the tables, every step adds its own settings row
/// (<c>step-v2</c> …) and records itself in the <c>fake-steps</c> row. <see cref="Fail"/> makes one step write its row and then throw.
/// </summary>
internal sealed class FakeSchemaSteps(params string[] steps) : ISchemaSteps
{
    public const string Record = "fake-steps";

    /// <summary>The step that fails after it changed the file (null = none).</summary>
    public string? Fail { get; init; }

    /// <summary>The steps applied by this instance, in order.</summary>
    public List<string> Ran { get; } = [];

    public IReadOnlyList<string> Known(ConceptDbContext db) => steps;

    public async Task<IReadOnlyList<string>> AppliedAsync(ConceptDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.SchemaObjects.AnyAsync(o => o.Type == ConceptSchema.TableType && o.Name == ConceptSchema.Settings, cancellationToken))
        {
            return [];
        }

        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Record, cancellationToken);
        return row is null ? [] : row.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }

    public async Task ApplyAsync(ConceptDbContext db, string step, CancellationToken cancellationToken)
    {
        if (step == steps[0])
        {
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
        }

        db.Settings.Add(new SettingRow { Key = "step-" + step, Value = "done" });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        if (step == Fail)
        {
            throw new InvalidOperationException("The step " + step + " broke.");
        }

        var applied = await AppliedAsync(db, cancellationToken);
        await db.UpsertAsync(new SettingRow { Key = Record, Value = string.Join(',', applied.Append(step)) }, s => s.Key == Record, cancellationToken);
        db.ChangeTracker.Clear();
        Ran.Add(step);
    }
}
