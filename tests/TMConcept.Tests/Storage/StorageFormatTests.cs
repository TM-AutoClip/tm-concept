using Shouldly;
using TMConcept.Core.Runs;
using TMConcept.Storage.Database;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>Since 0.1.3 the database is written without EF Core; values must keep the format of the 0.1.0–0.1.2 files.</summary>
public sealed class StorageFormatTests
{
    [Fact]
    public async Task ValuesAreStoredAsTheEarlierVersionsStoredThem()
    {
        using var h = await StorageHarness.CreatedAsync();
        var id = Guid.NewGuid();
        var started = new DateTimeOffset(2026, 11, 5, 21, 30, 0, TimeSpan.FromHours(3));
        var runs = new RunStore(h.Database);
        await runs.AddAsync(new Run { Id = id, EstimateMinUsd = 1m, EstimateMaxUsd = 12.5m, ActualUsd = 0.0123m, CostExact = true, StartedAtUtc = started,
            Kind = RunKind.Concepts, Status = RunStatus.Succeeded }, CancellationToken.None);

        await using var db = await h.Database.OpenAsync(CancellationToken.None);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT typeof(\"Id\"), \"Id\", \"EstimateMinUsd\", \"EstimateMaxUsd\", \"ActualUsd\", \"CostExact\", \"StartedAtUtc\", " +
            "typeof(\"FinishedAtUtc\"), \"Kind\", \"Status\" FROM \"runs\"";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();
        reader.GetString(0).ShouldBe("text");
        reader.GetString(1).ShouldBe(id.ToString().ToUpperInvariant());
        reader.GetString(2).ShouldBe("1.0");
        reader.GetString(3).ShouldBe("12.5");
        reader.GetString(4).ShouldBe("0.0123");
        reader.GetInt64(5).ShouldBe(1);
        reader.GetInt64(6).ShouldBe(started.UtcTicks);
        reader.GetString(7).ShouldBe("null");
        reader.GetInt32(8).ShouldBe((int)RunKind.Concepts);
        reader.GetInt32(9).ShouldBe((int)RunStatus.Succeeded);

        var back = (await runs.ListAsync(started.AddDays(-1), CancellationToken.None)).Single();
        back.Id.ShouldBe(id);
        back.ActualUsd.ShouldBe(0.0123m);
        back.StartedAtUtc.ShouldBe(started);
        back.FinishedAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task ANewFileGetsTheSchemaOfTheEarlierVersions()
    {
        using var h = await StorageHarness.CreatedAsync();
        await using var db = await h.Database.OpenAsync(CancellationToken.None);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT \"sql\" FROM sqlite_master WHERE \"name\" = 'concepts'";
        var sql = (string)(await command.ExecuteScalarAsync(CancellationToken.None))!;

        sql.ShouldContain("CONSTRAINT \"FK_concepts_profiles_ProfileId\" FOREIGN KEY (\"ProfileId\") REFERENCES \"profiles\" (\"Id\") ON DELETE CASCADE");
        (await h.CountAsync("sqlite_master", "type = 'index' AND substr(name, 1, 3) = 'IX_'")).ShouldBe(11);
        (await h.CountAsync("sqlite_master", "type = 'table'")).ShouldBe(13);

        // Opening again leaves an existing schema alone.
        await ConceptDatabase.EnsureSchemaAsync(db, CancellationToken.None);
        (await h.CountAsync("sqlite_master", "type = 'table'")).ShouldBe(13);
    }
}
