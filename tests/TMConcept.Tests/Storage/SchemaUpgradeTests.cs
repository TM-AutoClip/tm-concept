using Shouldly;
using TMConcept.Storage.Database;

namespace TMConcept.Tests.Storage;

/// <summary>A database file of 0.1.0–0.1.4 opens in 0.1.5: the new table is added, the voice columns and price overrides go, profiles stay.</summary>
public sealed class SchemaUpgradeTests
{
    private const string OldProfileColumns = """
            "TemplateId" TEXT NULL,
            "SpeechEngine" TEXT NOT NULL,
            "SpeechVoice" TEXT NOT NULL,
            "SpeechSpeed" REAL NOT NULL,
            "ConceptCounter" INTEGER NOT NULL,
        """;

    private const string OldOverrides = """
        CREATE TABLE "model_overrides" (
            "Provider" TEXT NOT NULL,
            "ModelId" TEXT NOT NULL,
            "InputUsd" TEXT NOT NULL,
            "OutputUsd" TEXT NOT NULL,
            "CacheReadUsd" TEXT NOT NULL,
            "Note" TEXT NOT NULL,
            "UpdatedAtUtc" INTEGER NOT NULL,
            CONSTRAINT "PK_model_overrides" PRIMARY KEY ("Provider", "ModelId")
        );
        """;

    [Fact]
    public async Task AFileOfAnEarlierVersionIsBroughtToTheCurrentSchema()
    {
        using var paths = new TempPaths();
        var connection = ConceptDatabase.ConnectionString(paths.DatabaseFile, "an old database");
        var start = ConceptSchema.Create.IndexOf("CREATE TABLE \"profile_step_models\"", StringComparison.Ordinal);
        var end = ConceptSchema.Create.IndexOf("CREATE TABLE \"concepts\"", StringComparison.Ordinal);
        var old = ConceptSchema.Create.Remove(start, end - start)
            .Replace("    \"TemplateId\" TEXT NULL,\n    \"ConceptCounter\" INTEGER NOT NULL,", OldProfileColumns, StringComparison.Ordinal) + OldOverrides;
        old.ShouldContain("SpeechSpeed");
        await using (var db = await ConceptDatabase.OpenAsync(connection, CancellationToken.None))
        {
            await Sql.ExecuteAsync(db, old, CancellationToken.None);
            await Sql.ExecuteAsync(db, """
                INSERT INTO "profiles" VALUES ('11111111-1111-1111-1111-111111111111', 'Old', 'en', 0, 75, 'x', NULL, 'supertonic', '3', 1.0, 4, 0, 0);
                INSERT INTO "step_models" VALUES ('image-prompt', 'anthropic', 'claude-haiku-5-5', NULL, '{}');
                INSERT INTO "step_models" VALUES ('concepts', 'anthropic', 'claude-sonnet-5-5', NULL, '{}');
                """, CancellationToken.None);

            await ConceptDatabase.EnsureSchemaAsync(db, CancellationToken.None);
            await ConceptDatabase.EnsureSchemaAsync(db, CancellationToken.None);
        }

        (await StorageHarness.CountAsync(connection, "sqlite_master", "name = 'profile_step_models'")).ShouldBe(1);
        (await StorageHarness.CountAsync(connection, "sqlite_master", "name = 'model_overrides'")).ShouldBe(0);
        (await StorageHarness.CountAsync(connection, "profiles")).ShouldBe(1);
        (await StorageHarness.CountAsync(connection, "step_models")).ShouldBe(1, "the removed steps are gone, concepts stays");
        await using (var db = await ConceptDatabase.OpenAsync(connection, CancellationToken.None))
        {
            (await Sql.ScalarAsync(db, "SELECT COUNT(*) FROM pragma_table_info('profiles') WHERE name LIKE 'Speech%'", CancellationToken.None)).ShouldBe(0);
            var profile = (await ProfileTable.Instance.ListAsync(db, string.Empty, CancellationToken.None)).Single();
            profile.Name.ShouldBe("Old");
            profile.ConceptCounter.ShouldBe(4);
        }
    }
}
