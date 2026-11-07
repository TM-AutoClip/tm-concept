using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>
/// The files of the released versions (<see cref="OldFiles"/>: written by 0.1.2, 0.1.5 and 0.1.6 themselves) open with this version: an
/// older one is brought to the current schema with every value kept, a new file and an upgraded one have the same schema as the 0.1.6 file
/// (tables, columns, keys, indexes), and a restored backup of an older version is upgraded before it is used (0.1.5 skipped that: saving a
/// profile after restoring a 0.1.4 backup failed with "no such table: profile_step_models").
/// </summary>
public sealed class SchemaUpgradeTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData(OldFiles.Version1, 1)]
    [InlineData(OldFiles.Version2, 2)]
    [InlineData(OldFiles.Version3, 3)]
    public async Task AFileOfAReleasedVersionOpensWithEveryValue(string file, int version)
    {
        using var h = OldFiles.Harness(file);
        await using (var db = await h.Database.OpenAsync(None))
        {
            (await ConceptDatabase.VersionAsync(db, None)).ShouldBe(version);
            await ConceptDatabase.EnsureSchemaAsync(db, None);
            await ConceptDatabase.EnsureSchemaAsync(db, None);
            (await ConceptDatabase.VersionAsync(db, None)).ShouldBe(ConceptSchema.Version);
            (await db.SchemaObjects.CountAsync(o => o.Name == ConceptSchema.PriceOverrides || o.Name == ConceptSchema.GeneralStepModels, None)).ShouldBe(0);
            (await db.TableColumns.CountAsync(c => c.Table == ConceptSchema.Profiles && c.Name.StartsWith("Speech"), None)).ShouldBe(0);
        }

        await OldFileValues.ShouldHoldEverythingAsync(h, file);

        // And it is written to as usual: the next concept number follows the stored counter, a profile and a choice are saved.
        await new ConceptStore(h.Database).AddAsync([new Concept { ProfileId = OldFileValues.Profile1, Title = "Next", Language = "tr" }], None);
        (await new ProfileStore(h.Database).GetAsync(OldFileValues.Profile1, None))!.ConceptCounter.ShouldBe(3);
        (await new ConceptStore(h.Database).HistoryAsync(OldFileValues.Profile1, None)).Last().Number.ShouldBe(3);
        await new ProfileStore(h.Database).SaveAsync(new Profile { Name = "After the upgrade", ConceptText = "x" }, None);
        await new StepModelStore(h.Database).SaveAsync(new StepModelChoice { Step = "video", ProfileId = OldFileValues.Profile2, Provider = "anthropic", Model = "m" }, None);
        (await new StepModelStore(h.Database).GetAsync("video", OldFileValues.Profile2, None))!.Model.ShouldBe("m");
    }

    [Fact]
    public async Task ANewFileAndUpgradedFilesHaveTheSchemaOfThe016File()
    {
        using var fresh = await StorageHarness.CreatedAsync();
        var released = await SchemaAsync(OldFiles.Harness(OldFiles.Version3), upgrade: false);
        var created = await SchemaAsync(fresh, upgrade: false);

        released.Count.ShouldBeGreaterThan(100);
        created.ShouldBe(released);
        (await SchemaAsync(OldFiles.Harness(OldFiles.Version1), upgrade: true)).ShouldBe(released);
        (await SchemaAsync(OldFiles.Harness(OldFiles.Version2), upgrade: true)).ShouldBe(released);
    }

    [Fact]
    public async Task EveryTableTheAppMapsIsReadableInANewAndInAnUpgradedFile()
    {
        using var fresh = await StorageHarness.CreatedAsync();
        using var old = OldFiles.Harness(OldFiles.Version1);
        foreach (var h in new[] { fresh, old })
        {
            await using var db = await h.Database.OpenAsync(None);
            await ConceptDatabase.EnsureSchemaAsync(db, None);
            await Should.NotThrowAsync(async () =>
            {
                await db.Profiles.AnyAsync(None);
                await db.ProfileImages.AnyAsync(None);
                await db.Concepts.AnyAsync(None);
                await db.Embeddings.AnyAsync(None);
                await db.HistoryDigests.AnyAsync(None);
                await db.Runs.AnyAsync(None);
                await db.QueueItems.AnyAsync(None);
                await db.Videos.AnyAsync(None);
                await db.VideoCosts.AnyAsync(None);
                await db.ApiKeys.AnyAsync(None);
                await db.ProfileStepModels.AnyAsync(None);
                await db.Settings.AnyAsync(None);
            });
        }
    }

    [Fact]
    public async Task DeletedDataIsOverwrittenWithZeros()
    {
        using var h = await StorageHarness.CreatedAsync();
        (await h.QueryAsync(db => db.SecureDelete.Select(s => s.Value).SingleAsync())).ShouldBe(ConceptSchema.SecureDeleteOn);
    }

    [Fact]
    public async Task AFileOfANewerVersionIsRefusedAndNotChanged()
    {
        using var h = OldFiles.Harness(OldFiles.Version3);
        await h.QueryAsync(async db =>
        {
            db.Settings.Add(new SettingRow { Key = ConceptSchema.VersionSetting, Value = "4" });
            return await db.SaveChangesAsync(None);
        });

        var before = await File.ReadAllBytesAsync(h.Paths.DatabaseFile, None);
        var refused = await Should.ThrowAsync<InvalidOperationException>(() => h.QueryAsync(async db =>
        {
            await ConceptDatabase.EnsureSchemaAsync(db, None);
            return 0;
        }));
        refused.Message.ShouldContain("newer version");
        SqliteConnection.ClearAllPools();
        (await File.ReadAllBytesAsync(h.Paths.DatabaseFile, None)).ShouldBe(before);
    }

    [Fact]
    public async Task ABackupOfVersion014IsUpgradedWhenItIsRestoredAndAProfileCanBeSavedRightAway()
    {
        using var source = new TempPaths();
        var bytes = await File.ReadAllBytesAsync(OldFiles.CopyTo(OldFiles.Version1, Path.Combine(source.DataDirectory, "old.db")), None);
        var plain = new byte[33 + bytes.Length];
        plain[0] = 32;
        OldFiles.Key.CopyTo(plain, 1);
        bytes.CopyTo(plain, 33);
        var file = Path.Combine(source.DataDirectory, "old" + BackupRules.Extension);
        await File.WriteAllBytesAsync(file, SealedBox.Seal(System.Text.Encoding.ASCII.GetBytes("TMCBAK02"), "backup password 123", plain, KdfParameters.Fast), None);

        using var target = new StorageHarness();
        var backup = new BackupService(target.Paths, target.Key, target.Vault, target.Database, KdfParameters.Fast, TimeProvider.System, NullLogger<BackupService>.Instance);
        await backup.RestoreAsync(file, "backup password 123", target.Protector, None);

        await OldFileValues.ShouldHoldEverythingAsync(target, OldFiles.Version1);
        var profiles = new ProfileStore(target.Database);
        await profiles.SaveAsync(new Profile { Name = "New after restore", ConceptText = "y" }, None);
        await new StepModelStore(target.Database).SaveAsync(
            new StepModelChoice { Step = "concepts", ProfileId = OldFileValues.Profile1, Provider = "anthropic", Model = "claude-haiku-5-5" }, None);
        (await profiles.ListAsync(None)).Count.ShouldBe(3);
    }

    /// <summary>The schema of a harness's file (upgraded first when asked), plus its version.</summary>
    private static async Task<List<string>> SchemaAsync(StorageHarness h, bool upgrade)
    {
        using var _ = h;
        await using (var db = await h.Database.OpenAsync(None))
        {
            if (upgrade)
            {
                await ConceptDatabase.EnsureSchemaAsync(db, None);
            }

            (await ConceptDatabase.VersionAsync(db, None)).ShouldBe(ConceptSchema.Version);
        }

        await using var inspect = InspectionContext.Open(h.ConnectionString);
        return await inspect.SchemaAsync();
    }
}
