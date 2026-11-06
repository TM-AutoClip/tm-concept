using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>
/// A new file and an upgraded one have the same schema (tables, columns, keys, indexes), every table the app reads exists with every column
/// it reads in both, and a restored backup of an older version is upgraded before it is used (0.1.5 skipped that: saving a profile after
/// restoring a 0.1.4 backup failed with "no such table: profile_step_models").
/// </summary>
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

    private const string StepModels = """
        CREATE TABLE "step_models" (
            "Step" TEXT NOT NULL CONSTRAINT "PK_step_models" PRIMARY KEY,
            "Provider" TEXT NOT NULL,
            "Model" TEXT NOT NULL,
            "KeyId" TEXT NULL,
            "OptionsJson" TEXT NOT NULL
        );
        """;

    private const string ProfileId = "11111111-1111-1111-1111-111111111111";

    /// <summary>The schema of 0.1.0–0.1.4 (no profile choices, voice columns, price overrides, general step models).</summary>
    private static string Version1()
    {
        var start = ConceptSchema.Create.IndexOf("CREATE TABLE \"profile_step_models\"", StringComparison.Ordinal);
        var end = ConceptSchema.Create.IndexOf("CREATE TABLE \"concepts\"", StringComparison.Ordinal);
        return ConceptSchema.Create.Remove(start, end - start)
            .Replace("    \"TemplateId\" TEXT NULL,\n    \"ConceptCounter\" INTEGER NOT NULL,", OldProfileColumns, StringComparison.Ordinal) + OldOverrides + StepModels;
    }

    /// <summary>The schema of 0.1.5 (profile choices and the general step models, no version number).</summary>
    private static string Version2() => ConceptSchema.Create + StepModels;

    private static async Task<string> FileAsync(TempPaths paths, string name, string? schema, string rows = "")
    {
        var connection = ConceptDatabase.ConnectionString(Path.Combine(paths.DataDirectory, name), "an old database");
        await using var db = await ConceptDatabase.OpenAsync(connection, CancellationToken.None);
        if (schema is not null)
        {
            await Sql.ExecuteAsync(db, schema + rows, CancellationToken.None);
        }

        await ConceptDatabase.EnsureSchemaAsync(db, CancellationToken.None);
        await ConceptDatabase.EnsureSchemaAsync(db, CancellationToken.None);
        return connection;
    }

    [Fact]
    public async Task AFileOfAnEarlierVersionIsBroughtToTheCurrentSchema()
    {
        using var paths = new TempPaths();
        Version1().ShouldContain("SpeechSpeed");
        var connection = await FileAsync(paths, "old.db", Version1(), $$"""
            INSERT INTO "profiles" VALUES ('{{ProfileId}}', 'Old', 'en', 0, 75, 'x', NULL, 'supertonic', '3', 1.0, 4, 0, 0);
            INSERT INTO "step_models" VALUES ('image-prompt', 'anthropic', 'claude-haiku-5-5', NULL, '{}');
            INSERT INTO "step_models" VALUES ('concepts', 'anthropic', 'claude-sonnet-5-5', NULL, '{}');
            """);

        (await StorageHarness.CountAsync(connection, "sqlite_master", "name IN ('model_overrides', 'step_models')")).ShouldBe(0);
        await using var db = await ConceptDatabase.OpenAsync(connection, CancellationToken.None);
        (await ConceptDatabase.VersionAsync(db, CancellationToken.None)).ShouldBe(ConceptSchema.Version);
        (await Sql.ScalarAsync(db, "SELECT COUNT(*) FROM pragma_table_info('profiles') WHERE name LIKE 'Speech%'", CancellationToken.None)).ShouldBe(0);
        var profile = (await ProfileTable.Instance.ListAsync(db, string.Empty, CancellationToken.None)).Single();
        profile.Name.ShouldBe("Old");
        profile.ConceptCounter.ShouldBe(4);

        // The general default became the profile's own choice; the removed step did not come along.
        var choices = await ProfileStepModelTable.Instance.ListAsync(db, string.Empty, CancellationToken.None);
        choices.Single().Model.ShouldBe("claude-sonnet-5-5");
    }

    [Fact]
    public async Task ANewFileAndUpgradedFilesHaveTheSameSchema()
    {
        using var paths = new TempPaths();
        var fresh = await SchemaAsync(await FileAsync(paths, "new.db", null));
        var from1 = await SchemaAsync(await FileAsync(paths, "v1.db", Version1()));
        var from2 = await SchemaAsync(await FileAsync(paths, "v2.db", Version2(),
            $"""INSERT INTO "profiles" VALUES ('{ProfileId}', 'P', 'en', 0, 75, 'x', NULL, 0, 0, 0);"""));

        fresh.ShouldNotBeEmpty();
        from1.ShouldBe(fresh);
        from2.ShouldBe(fresh);
    }

    [Fact]
    public async Task EveryTableAndColumnTheAppReadsExistsInANewAndInAnUpgradedFile()
    {
        using var paths = new TempPaths();
        var tables = typeof(ConceptDatabase).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.BaseType is { IsGenericType: true } b && b.GetGenericTypeDefinition().Name.StartsWith("TableBase", StringComparison.Ordinal))
            .Select(t => (Name: t.Name, Select: (string)t.BaseType!.GetProperty("Select")!.GetValue(t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null))!))
            .ToList();
        tables.Count.ShouldBeGreaterThanOrEqualTo(12);
        foreach (var connection in new[] { await FileAsync(paths, "new.db", null), await FileAsync(paths, "v1.db", Version1()) })
        {
            await using var db = await ConceptDatabase.OpenAsync(connection, CancellationToken.None);
            foreach (var (name, select) in tables)
            {
                await Should.NotThrowAsync(() => Sql.ExecuteAsync(db, select + " LIMIT 0", CancellationToken.None), name);
            }
        }
    }

    [Fact]
    public async Task ABackupOfVersion014IsUpgradedWhenItIsRestoredAndAProfileCanBeSavedRightAway()
    {
        using var source = new TempPaths();
        var oldDb = Path.Combine(source.DataDirectory, "old.db");
        var dek = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        await using (var db = await ConceptDatabase.OpenAsync(ConceptDatabase.ConnectionString(oldDb, Convert.ToHexString(dek)), CancellationToken.None))
        {
            await Sql.ExecuteAsync(db, Version1() + $"""
                INSERT INTO "profiles" VALUES ('{ProfileId}', 'Old', 'en', 0, 75, 'x', NULL, 'supertonic', '3', 1.0, 4, 0, 0);
                """, CancellationToken.None);
        }

        SqliteConnection.ClearAllPools();
        var bytes = await File.ReadAllBytesAsync(oldDb, TestContext.Current.CancellationToken);
        var plain = new byte[33 + bytes.Length];
        plain[0] = 32;
        dek.CopyTo(plain, 1);
        bytes.CopyTo(plain, 33);
        var file = Path.Combine(source.DataDirectory, "old" + BackupRules.Extension);
        await File.WriteAllBytesAsync(file, SealedBox.Seal(System.Text.Encoding.ASCII.GetBytes("TMCBAK02"), "backup password 123", plain, KdfParameters.Fast),
            TestContext.Current.CancellationToken);

        using var target = new StorageHarness();
        var backup = new BackupService(target.Paths, target.Key, target.Vault, target.Database, KdfParameters.Fast, TimeProvider.System, NullLogger<BackupService>.Instance);
        await backup.RestoreAsync(file, "backup password 123", target.Protector, CancellationToken.None);

        var profiles = new ProfileStore(target.Database);
        var profile = (await profiles.ListAsync(CancellationToken.None)).Single();
        await profiles.SaveAsync(new Profile { Name = "New after restore", ConceptText = "y" }, CancellationToken.None);
        await new StepModelStore(target.Database).SaveAsync(new StepModelChoice { Step = "concepts", ProfileId = profile.Id, Provider = "anthropic", Model = "claude-haiku-5-5" },
            CancellationToken.None);
        (await profiles.ListAsync(CancellationToken.None)).Count.ShouldBe(2);
    }

    /// <summary>Every table with its columns (name, type, not null, key), foreign keys and indexes, plus the version — in a comparable form.</summary>
    private static async Task<List<string>> SchemaAsync(string connection)
    {
        var lines = new List<string>();
        await using var db = await ConceptDatabase.OpenAsync(connection, CancellationToken.None);
        lines.Add("version " + await ConceptDatabase.VersionAsync(db, CancellationToken.None));
        var tables = new List<string>();
        await using (var command = db.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
            while (await reader.ReadAsync(CancellationToken.None))
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var table in tables)
        {
            foreach (var (query, label) in new[]
                     {
                         ($"SELECT name, type, \"notnull\", pk FROM pragma_table_info('{table}') ORDER BY cid", "column"),
                         ($"SELECT \"table\", \"from\", \"to\", on_delete FROM pragma_foreign_key_list('{table}') ORDER BY \"from\"", "fk"),
                         ($"SELECT name, \"unique\" FROM pragma_index_list('{table}') WHERE origin = 'c' ORDER BY name", "index"),
                     })
            {
                await using var command = db.CreateCommand();
                command.CommandText = query;
                await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
                while (await reader.ReadAsync(CancellationToken.None))
                {
                    lines.Add(table + " " + label + " " + string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i).ToString())));
                }
            }
        }

        return lines;
    }
}
