using Microsoft.Data.Sqlite;
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
        new(h.Paths, h.Key, h.Vault, h.Factory, new SettingsStore(h.Factory), KdfParameters.Fast, clock ?? TimeProvider.System, NullLogger<BackupService>.Instance);

    private static async Task<(StorageHarness H, string File)> BackupAsync()
    {
        var h = await StorageHarness.CreatedAsync();
        await new ProfileStore(h.Factory).SaveAsync(new Profile { Name = "Kept channel" }, None);
        await new ApiKeyStore(h.Factory).SaveAsync(new ApiKey { Provider = "openai", Label = "x", Secret = "never-in-backup-1" }, None);
        var service = Service(h);
        await service.SetPasswordAsync(Password, None);
        var file = Path.Combine(h.Paths.DataDirectory, "manual" + BackupRules.Extension);
        await service.CreateAsync(file, None);
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
        (await new ProfileStore(target.Factory).ListAsync(None)).Single().Name.ShouldBe("Kept channel");
        (await new ApiKeyStore(target.Factory).ListAsync(null, None)).ShouldBeEmpty();
        (await new ApiKeyStore(source.Factory).ListAsync(null, None)).ShouldNotBeEmpty();
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
    public async Task ShortPasswordIsRejected()
    {
        using var h = await StorageHarness.CreatedAsync();
        await Should.ThrowAsync<ArgumentException>(() => Service(h).SetPasswordAsync("short", None));
    }
}
