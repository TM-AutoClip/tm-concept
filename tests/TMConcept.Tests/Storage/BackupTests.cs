using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.Keys;
using TMConcept.Core.Profiles;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Keys;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

public sealed class BackupTests
{
    private const string Password = "backup password 123";
    private static readonly CancellationToken None = CancellationToken.None;

    private static BackupService Service(StorageHarness h, TimeProvider? clock = null) =>
        new(h.Paths, h.Key, h.Vault, h.Database, KdfParameters.Fast, clock ?? TimeProvider.System, NullLogger<BackupService>.Instance);

    private static async Task<(StorageHarness H, string File)> BackupAsync()
    {
        var h = await StorageHarness.CreatedAsync();
        await new ProfileStore(h.Database).SaveAsync(new Profile { Name = "Kept channel" }, None);
        await new ApiKeyStore(h.Database).SaveAsync(new ApiKey { Provider = "openai", Label = "x", Secret = "never-in-backup-1" }, None);
        var service = Service(h);
        await service.SetPasswordAsync(Password, None);
        var file = Path.Combine(h.Paths.DataDirectory, "manual" + BackupRules.Extension);
        await service.CreateAsync(file, BackupContents.Automatic, None);
        return (h, file);
    }

    [Fact]
    public async Task RestoreBringsDataBackWithoutApiKeys()
    {
        var (source, file) = await BackupAsync();
        using var _ = source;
        using var target = new StorageHarness();
        var report = await Service(target).RestoreAsync(file, Password, target.Protector, None);
        report.Profiles.ShouldBe(1);
        (await new ProfileStore(target.Database).ListAsync(None)).Single().Name.ShouldBe("Kept channel");
        (await new ApiKeyStore(target.Database).ListAsync(null, None)).ShouldBeEmpty();
        (await new ApiKeyStore(source.Database).ListAsync(null, None)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task WrongPasswordFails()
    {
        var (source, file) = await BackupAsync();
        using var _ = source;
        using var target = new StorageHarness();
        await Should.ThrowAsync<SealedBoxException>(() => Service(target).RestoreAsync(file, "not the password!!", target.Protector, None));
        target.Vault.State.ShouldBe(VaultState.NotCreated);
    }

    [Fact]
    public async Task DamagedOrTamperedFileFails()
    {
        var (source, file) = await BackupAsync();
        using var _ = source;
        var bytes = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);

        var body = (byte[])bytes.Clone();
        body[^40] ^= 0x55;
        var bodyFile = file + ".body";
        await File.WriteAllBytesAsync(bodyFile, body, TestContext.Current.CancellationToken);

        var header = (byte[])bytes.Clone();
        header[10] ^= 0x01;
        var headerFile = file + ".header";
        await File.WriteAllBytesAsync(headerFile, header, TestContext.Current.CancellationToken);

        using var target = new StorageHarness();
        await Should.ThrowAsync<SealedBoxException>(() => Service(target).RestoreAsync(bodyFile, Password, target.Protector, None));
        await Should.ThrowAsync<SealedBoxException>(() => Service(target).RestoreAsync(headerFile, Password, target.Protector, None));
        await Should.ThrowAsync<SealedBoxException>(() => Service(target).RestoreAsync(file + ".body", "x", target.Protector, None));
    }

    [Fact]
    public async Task RestoreIsRefusedWhenDataExists()
    {
        var (source, file) = await BackupAsync();
        using var _ = source;
        await Should.ThrowAsync<InvalidOperationException>(() => Service(source).RestoreAsync(file, Password, source.Protector, None));
    }

    [Fact]
    public async Task DailyBackupRunsOncePerDayAndKeepsTen()
    {
        using var h = await StorageHarness.CreatedAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        var service = Service(h, clock);
        (await service.RunDailyAsync(None)).ShouldBeNull();
        await service.SetPasswordAsync(Password, None);

        (await service.RunDailyAsync(None)).ShouldNotBeNull();
        clock.Advance(TimeSpan.FromHours(3));
        (await service.RunDailyAsync(None)).ShouldBeNull();

        for (var day = 0; day < 12; day++)
        {
            clock.Advance(TimeSpan.FromDays(1));
            (await service.RunDailyAsync(None)).ShouldNotBeNull();
        }

        SqliteConnection.ClearAllPools();
        service.List().Count.ShouldBe(BackupRules.Keep);
    }

    [Fact]
    public async Task TheBackupFileHoldsNoBackupKeyAndARestoreDerivesItAgainFromThePassword()
    {
        var (source, file) = await BackupAsync();
        using var _ = source;

        // Opened like an attacker with the password would: the sealed copy has no stored key and no API key.
        var inspect = await OpenCopyAsync(file, source.Paths.DataDirectory);
        string[] stored = ["backup-key", "backup-last"];
        (await StorageHarness.QueryAsync(inspect, db => db.Settings.CountAsync(s => stored.Contains(s.Key)))).ShouldBe(0);
        (await StorageHarness.QueryAsync(inspect, db => db.ApiKeys.CountAsync())).ShouldBe(0);

        SqliteConnection.ClearAllPools();
        using var target = new StorageHarness();
        var service = Service(target);
        await service.RestoreAsync(file, Password, target.Protector, None);

        // Daily backups go on without asking, and the new file opens with the same password.
        (await service.HasPasswordAsync(None)).ShouldBeTrue();
        var next = await service.CreateInFolderAsync(null, None);
        var nextFile = await File.ReadAllBytesAsync(next, None);
        SealedBox.Open(System.Text.Encoding.ASCII.GetBytes("TMCBAK03"), Password, nextFile).Length.ShouldBeGreaterThan(36);
    }

    [Fact]
    public async Task WhatIsChosenGoesInAndARestoreBringsBackExactlyThat()
    {
        var (source, _) = await BackupAsync();
        using var __ = source;
        var profile = (await new ProfileStore(source.Database).ListAsync(None)).Single();
        Directory.CreateDirectory(Path.Combine(source.Paths.LibraryDirectory, "Kept channel", "images"));
        await File.WriteAllBytesAsync(Path.Combine(source.Paths.LibraryDirectory, "Kept channel", "images", "a.png"), [1, 2, 3, 4], None);
        await new ProfileStore(source.Database).SaveImageAsync(new ProfileImage { ProfileId = profile.Id, FileName = "Kept channel/images/a.png" }, None);
        await new StepModelStore(source.Database).SaveAsync(new TMConcept.Core.Models.StepModelChoice
        {
            Step = "concepts",
            ProfileId = profile.Id,
            Provider = "anthropic",
            Model = "claude-haiku-5-5",
        }, None);
        var service = Service(source);
        var withKeys = Path.Combine(source.Paths.DataDirectory, "keys" + BackupRules.Extension);
        var keysOnly = Path.Combine(source.Paths.DataDirectory, "only-keys" + BackupRules.Extension);
        await service.CreateAsync(withKeys, new BackupContents { Keys = true }, None);
        await service.CreateAsync(keysOnly, new BackupContents { Keys = true, Profiles = false, Concepts = false, Settings = false }, None);
        await Should.ThrowAsync<ArgumentException>(() => service.CreateAsync(keysOnly, new BackupContents { Profiles = false }, None));

        // Everything with the keys: the restore says so first, then brings back keys, profile, its model choice and its image file.
        using (var target = new StorageHarness())
        {
            var held = await Service(target).InspectAsync(withKeys, Password, None);
            (held.Keys, held.Profiles, held.Images, held.Contents.Keys).ShouldBe((1, 1, 1, true));
            target.Vault.State.ShouldBe(VaultState.NotCreated, "looking inside restores nothing");
            var report = await Service(target).RestoreAsync(withKeys, Password, target.Protector, None);
            report.Keys.ShouldBe(1);
            (await new ApiKeyStore(target.Database).ListAsync("openai", None)).Single().Secret.ShouldBe("never-in-backup-1");
            (await new StepModelStore(target.Database).GetAsync("concepts", profile.Id, None))!.Model.ShouldBe("claude-haiku-5-5");
            (await File.ReadAllBytesAsync(Path.Combine(target.Paths.LibraryDirectory, "Kept channel", "images", "a.png"), None)).ShouldBe([1, 2, 3, 4]);
        }

        // Only the keys: no profile comes back, no image file is written.
        using (var target = new StorageHarness())
        {
            var report = await Service(target).RestoreAsync(keysOnly, Password, target.Protector, None);
            (report.Keys, report.Profiles, report.Images).ShouldBe((1, 0, 0));
            (await new ProfileStore(target.Database).ListAsync(None)).ShouldBeEmpty();
            File.Exists(Path.Combine(target.Paths.LibraryDirectory, "Kept channel", "images", "a.png")).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task AutomaticBackupsHoldEverythingButTheKeysUntilAnotherChoiceIsSaved()
    {
        using var h = await StorageHarness.CreatedAsync();
        await new ApiKeyStore(h.Database).SaveAsync(new ApiKey { Provider = "openai", Label = "x", Secret = "kept-in-this-one-1" }, None);
        var service = Service(h);
        await service.SetPasswordAsync(Password, None);
        (await service.AutomaticContentsAsync(None)).ToString().ShouldBe("profiles, concepts, settings");
        var first = await service.CreateInFolderAsync(null, None);
        (await service.InspectAsync(first, Password, None)).Keys.ShouldBe(0);

        await service.SaveAutomaticContentsAsync(new BackupContents { Keys = true }, None);
        var second = await service.CreateBeforeChangeAsync("test", None);
        (await service.InspectAsync(second!, Password, None)).Keys.ShouldBe(1);
    }

    [Fact]
    public async Task ABackupOfVersion015StillRestores()
    {
        var (source, _) = await BackupAsync();
        using var __ = source;
        SqliteConnection.ClearAllPools();
        var database = await File.ReadAllBytesAsync(source.Paths.DatabaseFile, None);
        var dek = source.Key.Copy();
        var plain = new byte[33 + database.Length];
        plain[0] = 32;
        dek.CopyTo(plain, 1);
        database.CopyTo(plain, 33);
        var old = Path.Combine(source.Paths.DataDirectory, "old" + BackupRules.Extension);
        await File.WriteAllBytesAsync(old, SealedBox.Seal(System.Text.Encoding.ASCII.GetBytes("TMCBAK02"), Password, plain, KdfParameters.Fast), None);

        using var target = new StorageHarness();
        (await Service(target).InspectAsync(old, Password, None)).Profiles.ShouldBe(1);
        var report = await Service(target).RestoreAsync(old, Password, target.Protector, None);
        report.Profiles.ShouldBe(1);
        report.Contents.Keys.ShouldBeFalse();
    }

    [Fact]
    public async Task ChangingTheBackupPasswordNeedsTheCurrentOne()
    {
        using var h = await StorageHarness.CreatedAsync();
        var service = Service(h);
        await service.SetPasswordAsync(Password, None);

        (await service.ChangePasswordAsync("not the current one", "a brand new password", None)).ShouldBeFalse();
        (await service.VerifyPasswordAsync(Password, None)).ShouldBeTrue("a wrong current password changes nothing");

        (await service.ChangePasswordAsync(Password, "a brand new password", None)).ShouldBeTrue();
        (await service.VerifyPasswordAsync("a brand new password", None)).ShouldBeTrue();
        (await service.VerifyPasswordAsync(Password, None)).ShouldBeFalse();
    }

    /// <summary>Opens a format 03 backup by hand and writes its database copy out; returns its connection string.</summary>
    private static async Task<string> OpenCopyAsync(string file, string folder)
    {
        var sealedData = await File.ReadAllBytesAsync(file, None);
        var plain = SealedBox.Open(System.Text.Encoding.ASCII.GetBytes("TMCBAK03"), Password, sealedData);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(plain);
        using var header = System.Text.Json.JsonDocument.Parse(plain.AsMemory(4, length));
        var databaseLength = header.RootElement.GetProperty("DatabaseLength").GetInt32();
        var passphrase = Convert.ToHexString(plain, 4 + length, 32);
        var copy = Path.Combine(folder, "inspect-" + Guid.NewGuid().ToString("N") + ".db");
        await File.WriteAllBytesAsync(copy, plain.AsMemory(4 + length + 32, databaseLength).ToArray(), None);
        return TMConcept.Storage.Database.ConceptDatabase.ConnectionString(copy, passphrase);
    }

    [Fact]
    public async Task SavingOlderSettingsDoesNotUndoTheLastBackupTime()
    {
        using var h = await StorageHarness.CreatedAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        var settings = new SettingsStore(h.Database);
        var stale = await settings.GetAsync(None);
        var service = Service(h, clock);
        await service.SetPasswordAsync(Password, None);

        (await service.RunDailyAsync(None)).ShouldNotBeNull();
        await settings.SaveAsync(stale, None);
        clock.Advance(TimeSpan.FromHours(1));

        (await service.LastBackupAsync(None)).ShouldBe(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        (await service.RunDailyAsync(None)).ShouldBeNull();
    }

    [Fact]
    public async Task BackupsBeforeAChangeAreKeptApartFromTheDailyOnes()
    {
        using var h = await StorageHarness.CreatedAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        var service = Service(h, clock);
        (await service.CreateBeforeChangeAsync("test", None)).ShouldBeNull();
        await service.SetPasswordAsync(Password, None);
        (await service.RunDailyAsync(None)).ShouldNotBeNull();

        for (var i = 0; i < 12; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            Path.GetFileName(await service.CreateBeforeChangeAsync("test", None))!.ShouldStartWith(BackupRules.ChangePrefix);
        }

        SqliteConnection.ClearAllPools();
        var files = service.List().Select(f => Path.GetFileName(f.Path)).ToList();
        files.Count(f => f.StartsWith(BackupRules.ChangePrefix, StringComparison.Ordinal)).ShouldBe(BackupRules.Keep);
        files.Count(f => !f.StartsWith(BackupRules.ChangePrefix, StringComparison.Ordinal)).ShouldBe(1, "the daily backup is not pushed out");
    }

    [Fact]
    public async Task TheBackupBeforeAChangeAlwaysHoldsTheProfilesAndConcepts()
    {
        var (h, _) = await BackupAsync();
        using var __ = h;
        var service = Service(h);
        await service.SaveAutomaticContentsAsync(new BackupContents { Settings = true }, None);

        var file = await service.CreateBeforeChangeAsync("test", None);

        var held = await service.InspectAsync(file!, Password, None);
        (held.Contents.Profiles, held.Contents.Concepts, held.Contents.Settings, held.Contents.Keys).ShouldBe((true, true, true, false));
        held.Profiles.ShouldBe(1, "the profile a delete would remove is in it");
    }

    [Fact]
    public async Task BackUpNowHasItsOwnFilesAndNeverStandsInForTheDailyBackup()
    {
        using var h = await StorageHarness.CreatedAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        var service = Service(h, clock);
        await service.SetPasswordAsync(Password, None);
        (await service.RunDailyAsync(None)).ShouldNotBeNull();
        clock.Advance(TimeSpan.FromDays(1));

        for (var i = 0; i < 12; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            Path.GetFileName(await service.CreateInFolderAsync(new BackupContents { Keys = true }, None)).ShouldStartWith(BackupRules.ManualPrefix);
        }

        (await service.LastBackupAsync(None)).ShouldBe(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), "a keys-only backup is not the daily one");
        (await service.LastAnyBackupAsync(None))!.Value.ShouldBeGreaterThan(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        (await service.RunDailyAsync(None)).ShouldNotBeNull("the complete daily backup is still written today");

        SqliteConnection.ClearAllPools();
        var files = service.List().Select(f => Path.GetFileName(f.Path)).ToList();
        files.Count(f => f.StartsWith(BackupRules.ManualPrefix, StringComparison.Ordinal)).ShouldBe(BackupRules.Keep);
        files.Count(f => !f.StartsWith(BackupRules.ManualPrefix, StringComparison.Ordinal)).ShouldBe(2, "the daily backups are not pushed out");
    }

    [Fact]
    public async Task ABackupOfANewerVersionIsRefusedAndAnotherRestoreCanBeTried()
    {
        var (source, file) = await BackupAsync();
        using var _ = source;
        await SchemaVersionAsync(source, TMConcept.Storage.Database.ConceptSchema.Version + 1);

        var newer = Path.Combine(source.Paths.DataDirectory, "newer" + BackupRules.Extension);
        await Service(source).CreateAsync(newer, BackupContents.Automatic, None);

        using var target = new StorageHarness();
        await Should.ThrowAsync<BackupRefusedException>(() => Service(target).RestoreAsync(newer, Password, target.Protector, None));
        target.Vault.State.ShouldBe(VaultState.NotCreated);
        File.Exists(target.Paths.DatabaseFile).ShouldBeFalse();

        await SchemaVersionAsync(source, TMConcept.Storage.Database.ConceptSchema.Version);

        var current = Path.Combine(source.Paths.DataDirectory, "current" + BackupRules.Extension);
        await Service(source).CreateAsync(current, BackupContents.Automatic, None);
        (await Service(target).RestoreAsync(current, Password, target.Protector, None)).Profiles.ShouldBe(1);
    }

    /// <summary>Sets the schema version row of a harness's database (what a newer version would write).</summary>
    private static Task<int> SchemaVersionAsync(StorageHarness h, int version) => h.QueryAsync(db =>
        db.Settings.Where(s => s.Key == TMConcept.Storage.Database.ConceptSchema.VersionSetting)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Value, version.ToString(System.Globalization.CultureInfo.InvariantCulture))));

    [Fact]
    public async Task ShortPasswordIsRejected()
    {
        using var h = await StorageHarness.CreatedAsync();
        await Should.ThrowAsync<ArgumentException>(() => Service(h).SetPasswordAsync("short", None));
    }
}
