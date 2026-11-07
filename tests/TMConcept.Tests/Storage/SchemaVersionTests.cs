using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Storage.Database;
using TMConcept.Storage.Keys;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>
/// Only a database of this schema version is opened (no backward compatibility): an older file (0.1.0–0.1.6 wrote no version row) or a
/// newer one is refused unchanged, and setting it aside renames it so the app starts fresh.
/// </summary>
public sealed class SchemaVersionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task AFileWithoutTheVersionRowIsRefusedAndLeftUnchanged()
    {
        using var h = await StorageHarness.CreatedAsync();
        await new ProfileStore(h.Database).SaveAsync(new Profile { Name = "Kept" }, None);

        // What 0.1.6 left: the tables, but no schema-version row.
        await h.QueryAsync(db => db.Settings.Where(s => s.Key == ConceptSchema.VersionSetting).ExecuteDeleteAsync());
        SqliteConnection.ClearAllPools();
        var before = await File.ReadAllBytesAsync(h.Paths.DatabaseFile, None);
        h.Vault.Lock();

        var refused = await Should.ThrowAsync<DatabaseVersionException>(() => h.Vault.OpenAsync(None));

        refused.Found.ShouldBe(0);
        refused.Newer.ShouldBeFalse();
        h.Vault.State.ShouldBe(VaultState.Locked);
        SqliteConnection.ClearAllPools();
        (await File.ReadAllBytesAsync(h.Paths.DatabaseFile, None)).ShouldBe(before, "nothing is upgraded or written");
    }

    [Fact]
    public async Task ANewerOrOlderVersionNumberIsRefused()
    {
        using var h = await StorageHarness.CreatedAsync();

        // A refused open forgets the key; the test loads it again to change the row.
        var dek = h.Key.Copy();
        foreach (var version in new[] { ConceptSchema.Version + 1, ConceptSchema.Version - 1 })
        {
            h.Key.Load(dek);
            await SetVersionAsync(h, version);
            h.Vault.Lock();
            var refused = await Should.ThrowAsync<DatabaseVersionException>(() => h.Vault.OpenAsync(None));
            refused.Found.ShouldBe(version);
            refused.Newer.ShouldBe(version > ConceptSchema.Version);
        }

        h.Key.Load(dek);
        await SetVersionAsync(h, ConceptSchema.Version);
        h.Vault.Lock();
        await h.Vault.OpenAsync(None);
        h.Vault.State.ShouldBe(VaultState.Open);
    }

    [Fact]
    public async Task SettingAsideRenamesTheDatabaseAndItsKeyAndTheNextStartIsFresh()
    {
        using var h = await StorageHarness.CreatedAsync();
        await new ProfileStore(h.Database).SaveAsync(new Profile { Name = "Old" }, None);
        await SetVersionAsync(h, ConceptSchema.Version + 1);
        h.Vault.Lock();
        await Should.ThrowAsync<DatabaseVersionException>(() => h.Vault.OpenAsync(None));

        var aside = h.Vault.SetAside();

        File.Exists(aside).ShouldBeTrue();
        File.Exists(h.Paths.DatabaseFile).ShouldBeFalse();
        File.Exists(h.Paths.KeyFile).ShouldBeFalse();
        Directory.GetFiles(Path.GetDirectoryName(h.Paths.KeyFile)!, Path.GetFileName(h.Paths.KeyFile) + ".set-aside-*").Length.ShouldBe(1);
        h.Vault.State.ShouldBe(VaultState.NotCreated);

        await h.Vault.CreateAsync(h.Protector, None);
        (await new ProfileStore(h.Database).ListAsync(None)).ShouldBeEmpty();
        File.Exists(aside).ShouldBeTrue("the old file stays where it was set aside");
    }

    [Fact]
    public async Task ANewFileHasTheCurrentVersionRow()
    {
        using var h = await StorageHarness.CreatedAsync();

        (await h.QueryAsync(db => ConceptDatabase.VersionAsync(db, None))).ShouldBe(ConceptSchema.Version);
    }

    private static Task<int> SetVersionAsync(StorageHarness h, int version) => h.QueryAsync(db =>
        db.Settings.Where(s => s.Key == ConceptSchema.VersionSetting)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Value, version.ToString(System.Globalization.CultureInfo.InvariantCulture))));
}
