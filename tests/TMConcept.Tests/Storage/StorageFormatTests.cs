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

        (await h.QueryAsync(db => db.SchemaObjects.CountAsync(o => o.Type == "index" && o.Name.StartsWith("IX_")))).ShouldBe(20, "10 (0.1.29 dropped the profile index of the sample images), the concept index of the runs (0.1.25: an AI edit names its concept), the language index of the pronunciations, the profile index of the topic suggestions (by time), the profile index of the AI-edit wishes, the concept and profile (by time) indexes of the video ratings and the four of the documents (profile and content, passages by document and by profile, vectors by profile and model)");
        (await h.QueryAsync(db => db.SchemaObjects.CountAsync(o => o.Type == ConceptSchema.TableType && !o.Name.StartsWith("__EFMigrations")))).ShouldBe(20, "0.1.25 added the kept page texts (document_pages), 0.1.29 dropped profile_images");
        (await h.QueryAsync(db => db.Database.GetAppliedMigrationsAsync())).Select(SchemaUpgrader.Name).ShouldBe(["Version0117", "Version0118", "Version0119", "Version0120", "Version0125Costs", "Version0125Documents", "Version0129", "Version0132"],
            "the file records the schema steps it went through");

        // Opening again leaves a current schema alone.
        var again = await h.QueryAsync(db => ConceptDatabase.EnsureSchemaAsync(db, CancellationToken.None));
        again.Applied.ShouldBeEmpty();
        (await h.QueryAsync(db => db.SchemaObjects.CountAsync(o => o.Type == ConceptSchema.TableType && !o.Name.StartsWith("__EFMigrations")))).ShouldBe(20);
        (await h.QueryAsync(db => db.Settings.CountAsync())).ShouldBe(0, "no settings row is written for a new file");
    }
}
