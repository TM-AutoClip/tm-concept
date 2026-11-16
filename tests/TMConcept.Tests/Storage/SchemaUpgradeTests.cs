using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Database;

namespace TMConcept.Tests.Storage;

/// <summary>
/// The chain of schema steps (developer decision, 16 Nov 2026): each app version that changes the tables adds one step that knows only the
/// schema before it; a file from several versions ago goes through every step in order; a copy is kept before an upgrade; a failed step
/// leaves the file as it was; a file the chain cannot read is reported and not changed.
/// </summary>
public sealed class SchemaUpgradeTests
{
    private const string Passphrase = "0011223344556677889900112233445566778899001122334455667788990011";

    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task ANewFileGetsEveryStepInOrderWithoutACopy()
    {
        using var paths = new TempPaths();
        var steps = new FakeSchemaSteps("v1", "v2", "v3");

        var result = await Upgrader(steps).UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None);

        steps.Ran.ShouldBe(["v1", "v2", "v3"]);
        result.From.ShouldBeEmpty();
        result.Applied.ShouldBe(["v1", "v2", "v3"]);
        result.Backup.ShouldBeNull("a new file has nothing to lose");
        (await AppliedAsync(paths.DatabaseFile)).ShouldBe(["v1", "v2", "v3"]);
    }

    [Fact]
    public async Task AFileSeveralVersionsOldGoesThroughEveryStepItLacksInOrderAfterACopy()
    {
        using var paths = new TempPaths();
        await Upgrader(new FakeSchemaSteps("v1")).UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None);
        var before = Hash(paths.DatabaseFile);

        // The user skipped the versions of v2: this app knows v1, v2 and v3.
        var steps = new FakeSchemaSteps("v1", "v2", "v3");
        var result = await Upgrader(steps).UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None);

        steps.Ran.ShouldBe(["v2", "v3"], "only what the file lacks, oldest first");
        result.From.ShouldBe("v1");
        result.Applied.ShouldBe(["v2", "v3"]);
        (await AppliedAsync(paths.DatabaseFile)).ShouldBe(["v1", "v2", "v3"]);
        (await RowsAsync(paths.DatabaseFile)).ShouldBe(["step-v1", "step-v2", "step-v3"]);

        // The copy is the file as it was before the upgrade, still encrypted with its key.
        var backup = result.Backup.ShouldNotBeNull();
        Path.GetDirectoryName(backup).ShouldBe(Backups(paths));
        Path.GetFileName(backup).ShouldEndWith("-v1.db");
        Hash(backup).ShouldBe(before);
        (await AppliedAsync(backup)).ShouldBe(["v1"]);
        File.Exists(paths.DatabaseFile + ConceptSchema.UpgradingSuffix).ShouldBeFalse();

        // Opened again: current, nothing runs and no copy is made.
        var again = new FakeSchemaSteps("v1", "v2", "v3");
        (await Upgrader(again).UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None)).Applied.ShouldBeEmpty();
        again.Ran.ShouldBeEmpty();
        Directory.GetFiles(Backups(paths)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task AFailedStepLeavesTheFileExactlyAsItWas()
    {
        using var paths = new TempPaths();
        await Upgrader(new FakeSchemaSteps("v1")).UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None);
        var before = Hash(paths.DatabaseFile);

        var steps = new FakeSchemaSteps("v1", "v2", "v3") { Fail = "v3" };
        var error = await Should.ThrowAsync<DatabaseVersionException>(() =>
            Upgrader(steps).UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None));

        error.Problem.ShouldBe(DatabaseVersionProblem.UpgradeFailed);
        error.Step.ShouldBe("v3");
        error.Message.ShouldContain("The step v3 broke.");
        steps.Ran.ShouldBe(["v2"], "v2 went through on the working copy before v3 failed");
        Hash(paths.DatabaseFile).ShouldBe(before, "neither v2 nor the half of v3 reached the file");
        (await RowsAsync(paths.DatabaseFile)).ShouldBe(["step-v1"]);
        File.Exists(paths.DatabaseFile + ConceptSchema.UpgradingSuffix).ShouldBeFalse("the working copy is removed");
        Directory.GetFiles(Backups(paths)).Length.ShouldBe(1, "the copy made before the upgrade stays");

        // The next start tries again from the same place.
        var retry = new FakeSchemaSteps("v1", "v2", "v3");
        await Upgrader(retry).UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None);
        retry.Ran.ShouldBe(["v2", "v3"]);
    }

    [Fact]
    public async Task AFileOfANewerVersionIsReportedAndNotChanged()
    {
        using var paths = new TempPaths();
        await Upgrader(new FakeSchemaSteps("v1", "v2", "v9")).UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None);
        var before = Hash(paths.DatabaseFile);

        var error = await Should.ThrowAsync<DatabaseVersionException>(() =>
            Upgrader(new FakeSchemaSteps("v1", "v2")).UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None));

        error.Problem.ShouldBe(DatabaseVersionProblem.Newer);
        error.Step.ShouldBe("v9");
        Hash(paths.DatabaseFile).ShouldBe(before);
        Directory.Exists(Backups(paths)).ShouldBeFalse("nothing was upgraded, so nothing was copied");
    }

    [Fact]
    public async Task AFileWithTablesButNoRecordIsReportedAndNotChanged()
    {
        using var paths = new TempPaths();
        await using (var db = await ConceptDatabase.OpenAsync(ConceptDatabase.ConnectionString(paths.DatabaseFile, Passphrase), None))
        {
            // What 0.1.16 did: the tables from the mapping, no record of any step.
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(None);
        }

        SqliteConnection.ClearAllPools();
        var before = Hash(paths.DatabaseFile);

        var error = await Should.ThrowAsync<DatabaseVersionException>(() =>
            Upgrader(new FakeSchemaSteps("v1", "v2")).UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None));

        error.Problem.ShouldBe(DatabaseVersionProblem.Unrecorded);
        error.Message.ShouldContain("0.1.16 or earlier");
        Hash(paths.DatabaseFile).ShouldBe(before);
    }

    [Fact]
    public async Task OnlyTheNewestCopiesAreKept()
    {
        using var paths = new TempPaths();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var known = new List<string> { "v01" };
        await new SchemaUpgrader(new FakeSchemaSteps([.. known]), NullLogger.Instance, clock).UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None);
        for (var i = 2; i <= ConceptSchema.UpgradeBackupsKept + 3; i++)
        {
            known.Add($"v{i:00}");
            clock.Advance(TimeSpan.FromMinutes(1));
            await new SchemaUpgrader(new FakeSchemaSteps([.. known]), NullLogger.Instance, clock)
                .UpgradeFileAsync(paths.DatabaseFile, Passphrase, Backups(paths), None);
        }

        var kept = Directory.GetFiles(Backups(paths)).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        kept.Count.ShouldBe(ConceptSchema.UpgradeBackupsKept);
        kept[^1].ShouldNotBeNull().ShouldEndWith($"-v{ConceptSchema.UpgradeBackupsKept + 2:00}.db"); // the newest copy: the file before the last step
    }

    [Fact]
    public async Task InPlaceTheChainRunsTheSameWay()
    {
        using var paths = new TempPaths();
        await using var db = await ConceptDatabase.OpenAsync(ConceptDatabase.ConnectionString(paths.DatabaseFile, Passphrase), None);
        await Upgrader(new FakeSchemaSteps("v1")).UpgradeInPlaceAsync(db, None);

        var steps = new FakeSchemaSteps("v1", "v2", "v3");
        (await Upgrader(steps).UpgradeInPlaceAsync(db, None)).Applied.ShouldBe(["v2", "v3"]);
        steps.Ran.ShouldBe(["v2", "v3"]);
    }

    [Fact]
    public void AStepIsNamedWithoutItsTime()
    {
        SchemaUpgrader.Name("20261116143956_Version0117").ShouldBe("Version0117");
        SchemaUpgrader.Name("v2").ShouldBe("v2");
    }

    [Fact]
    public async Task TheAppsStepsMatchItsMappingAndANewFileRecordsThem()
    {
        using var h = await StorageHarness.CreatedAsync();

        await h.QueryAsync(async db =>
        {
            // A mapping change without its step would fail here (and EF Core would refuse to upgrade with it).
            db.Database.HasPendingModelChanges().ShouldBeFalse("run tools/database-step.ps1 after a mapping change");
            var known = db.Database.GetMigrations().ToList();
            known.ShouldNotBeEmpty();
            known.Select(SchemaUpgrader.Name).First().ShouldBe("Version0117", "the first step is the schema of 0.1.17");
            known.Select(SchemaUpgrader.Name).ShouldAllBe(n => n.StartsWith("Version", StringComparison.Ordinal));
            (await db.Database.GetAppliedMigrationsAsync()).ShouldBe(known);
            return 0;
        });
    }

    [Fact]
    public async Task UnlockingADatabaseOf0116SaysSoAndLeavesItAlone()
    {
        using var h = await StorageHarness.CreatedAsync();
        var connection = h.ConnectionString;
        h.Vault.Lock();
        File.Delete(h.Paths.DatabaseFile);
        await using (var db = await ConceptDatabase.OpenAsync(connection, None))
        {
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(None);
        }

        SqliteConnection.ClearAllPools();
        var before = Hash(h.Paths.DatabaseFile);

        var error = await Should.ThrowAsync<DatabaseVersionException>(() => h.Vault.OpenAsync(None));

        error.Problem.ShouldBe(DatabaseVersionProblem.Unrecorded);
        h.Key.IsLoaded.ShouldBeFalse("a database that is not opened keeps no key in memory");
        Hash(h.Paths.DatabaseFile).ShouldBe(before);

        // Set aside: renamed with its key file, never deleted; the next step is a first start.
        var aside = h.Vault.SetAside();
        File.Exists(aside).ShouldBeTrue();
        File.Exists(h.Paths.DatabaseFile).ShouldBeFalse();
        File.Exists(h.Paths.KeyFile).ShouldBeFalse();
        h.Vault.State.ShouldBe(TMConcept.Storage.Keys.VaultState.NotCreated);
    }

    [Fact]
    public async Task ABackupMadeBeforeUpgradesWereRecordedIsRefusedBeforeAnythingChanges()
    {
        using var h = await StorageHarness.CreatedAsync();
        var backups = new BackupService(h.Paths, h.Key, h.Vault, h.Database, TMConcept.Storage.Crypto.KdfParameters.Fast, TimeProvider.System,
            NullLogger<BackupService>.Instance);

        // The database inside the backup: the tables without a record (as 0.1.16 wrote them).
        await h.QueryAsync(async db =>
        {
            await db.GetService<IMigrator>().MigrateAsync("0", None);
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(None);
            return 0;
        });
        await backups.SetPasswordAsync("backup password 1", None);
        var file = Path.Combine(h.Paths.DataDirectory, "old.tmcbak");
        await backups.CreateAsync(file, BackupContents.Automatic, None);

        var error = await Should.ThrowAsync<BackupRefusedException>(() => backups.InspectAsync(file, "backup password 1", None));
        error.Message.ShouldContain("0.1.16 or earlier");
    }

    private static SchemaUpgrader Upgrader(FakeSchemaSteps steps) => new(steps, NullLogger.Instance);

    private static string Backups(TempPaths paths) => Path.Combine(paths.BackupDirectory, ConceptSchema.UpgradeBackupFolder);

    private static async Task<IReadOnlyList<string>> AppliedAsync(string file)
    {
        await using var db = await ConceptDatabase.OpenAsync(ConceptDatabase.ConnectionString(file, Passphrase), None);
        var applied = await new FakeSchemaSteps().AppliedAsync(db, None);
        await db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        return applied;
    }

    private static async Task<List<string>> RowsAsync(string file)
    {
        await using var db = await ConceptDatabase.OpenAsync(ConceptDatabase.ConnectionString(file, Passphrase), None);
        var rows = await db.Settings.Where(s => s.Key.StartsWith("step-")).Select(s => s.Key).OrderBy(k => k).ToListAsync(None);
        await db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        return rows;
    }

    private static string Hash(string file)
    {
        SqliteConnection.ClearAllPools();
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    }
}
