using Microsoft.EntityFrameworkCore;
using Shouldly;
using TMConcept.Core.Runs;
using TMConcept.Storage.Database;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>How values and the schema are stored (TEXT ids, UTC ticks, invariant money text, 0/1 flags; EF Core creates the tables).</summary>
public sealed class StorageFormatTests
{
    [Fact]
    public async Task ValuesKeepTheirStoredForm()
    {
        using var h = await StorageHarness.CreatedAsync();
        var id = Guid.NewGuid();
        var started = new DateTimeOffset(2026, 11, 5, 21, 30, 0, TimeSpan.FromHours(3));
        var runs = new RunStore(h.Database);
        await runs.AddAsync(new Run { Id = id, EstimateMinUsd = 1m, EstimateMaxUsd = 12.5m, ActualUsd = 0.0123m, CostExact = true, StartedAtUtc = started,
            Kind = RunKind.Concepts, Status = RunStatus.Succeeded }, CancellationToken.None);

        await using (var raw = InspectionContext.Open(h.ConnectionString))
        {
            var row = await raw.Runs.Select(r => new
            {
                IdType = InspectionContext.SqliteType(r.Id),
                r.Id,
                r.EstimateMinUsd,
                r.EstimateMaxUsd,
                r.ActualUsd,
                r.CostExact,
                r.StartedAtUtc,
                FinishedType = InspectionContext.SqliteType(r.FinishedAtUtc),
                r.Kind,
                r.Status,
            }).SingleAsync(CancellationToken.None);
            row.IdType.ShouldBe("text");
            row.Id.ShouldBe(id.ToString().ToUpperInvariant());
            row.EstimateMinUsd.ShouldBe("1.0");
            row.EstimateMaxUsd.ShouldBe("12.5");
            row.ActualUsd.ShouldBe("0.0123");
            row.CostExact.ShouldBe(1);
            row.StartedAtUtc.ShouldBe(started.UtcTicks);
            row.FinishedType.ShouldBe("null");
            row.Kind.ShouldBe((int)RunKind.Concepts);
            row.Status.ShouldBe((int)RunStatus.Succeeded);
        }

        var back = (await runs.ListAsync(started.AddDays(-1), CancellationToken.None)).Single();
        back.Id.ShouldBe(id);
        back.ActualUsd.ShouldBe(0.0123m);
        back.StartedAtUtc.ShouldBe(started);
        back.FinishedAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task ANewFileGetsItsTablesIndexesAndCascades()
    {
        using var h = await StorageHarness.CreatedAsync();
        await using (var inspect = InspectionContext.Open(h.ConnectionString))
        {
            var sql = await inspect.Master.Where(m => m.Name == "concepts").Select(m => m.Sql).SingleAsync(CancellationToken.None);
            sql.ShouldNotBeNull().ShouldContain("CONSTRAINT \"FK_concepts_profiles_ProfileId\" FOREIGN KEY (\"ProfileId\") REFERENCES \"profiles\" (\"Id\") ON DELETE CASCADE");
        }

        (await h.QueryAsync(db => db.SchemaObjects.CountAsync(o => o.Type == "index" && o.Name.StartsWith("IX_")))).ShouldBe(14, "11, the language index of the pronunciations and the profile indexes of the topic suggestions and the AI-edit wishes");
        (await h.QueryAsync(db => db.SchemaObjects.CountAsync(o => o.Type == ConceptSchema.TableType))).ShouldBe(15);

        // Opening again leaves an existing schema alone.
        await h.QueryAsync(async db =>
        {
            await ConceptDatabase.EnsureSchemaAsync(db, CancellationToken.None);
            return 0;
        });
        (await h.QueryAsync(db => db.SchemaObjects.CountAsync(o => o.Type == ConceptSchema.TableType))).ShouldBe(15);
        (await h.QueryAsync(db => db.Settings.CountAsync())).ShouldBe(0, "no schema version or any other row is written for a new file");
    }
}
