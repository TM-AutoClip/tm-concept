using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Keys;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

public sealed class EncryptionTests
{
    [Fact]
    public async Task DatabaseFileIsEncryptedAndDoesNotOpenWithAnotherKey()
    {
        using var h = await StorageHarness.CreatedAsync();
        await new ProfileStore(h.Factory).SaveAsync(new Profile { Name = "secret-name-123" }, CancellationToken.None);
        SqliteConnection.ClearAllPools();

        var bytes = await File.ReadAllBytesAsync(h.Paths.DatabaseFile, TestContext.Current.CancellationToken);
        Encoding.ASCII.GetString(bytes, 0, 15).ShouldNotBe("SQLite format 3");
        Encoding.UTF8.GetString(bytes).ShouldNotContain("secret-name-123");
        Encoding.UTF8.GetString(bytes).ShouldNotContain("profiles");

        await using var other = new ConceptDbContext(ConceptDbContextFactory.Options(
            ConceptDbContextFactory.ConnectionString(h.Paths.DatabaseFile, Convert.ToHexString(new byte[32]))));
        await Should.ThrowAsync<SqliteException>(() => other.Profiles.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VaultOpensAgainWithTheRightPasswordOnly()
    {
        using var h = await StorageHarness.CreatedAsync();
        h.Vault.Lock();
        h.Vault.State.ShouldBe(VaultState.Locked);

        h.Passwords.Password = "wrong password!!";
        await Should.ThrowAsync<SealedBoxException>(() => h.Vault.OpenAsync(CancellationToken.None));
        h.Vault.State.ShouldBe(VaultState.Locked);

        h.Passwords.Password = "correct horse battery";
        await h.Vault.OpenAsync(CancellationToken.None);
        h.Vault.State.ShouldBe(VaultState.Open);
    }

    [Fact]
    public async Task DpapiProtectorRoundTripsOnWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI exists only on Windows.");
        var protector = new DpapiKeyProtector();
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var envelope = await protector.ProtectAsync(key, CancellationToken.None);
        envelope.Payload.ShouldNotContain(Convert.ToBase64String(key));
        (await protector.UnprotectAsync(envelope, CancellationToken.None)).ShouldBe(key);
    }

    [Fact]
    public async Task SecretServiceSendsTheKeyOnStandardInputOnly()
    {
        var runner = new FakeCommandRunner();
        var protector = new SecretServiceKeyProtector(runner);
        var key = Enumerable.Range(0, 32).Select(i => (byte)(i * 3)).ToArray();
        var envelope = await protector.ProtectAsync(key, CancellationToken.None);
        runner.Calls.Single().Arguments.ShouldNotContain(a => a.Contains(Convert.ToBase64String(key), StringComparison.Ordinal));
        runner.Calls.Single().Input.ShouldBe(Convert.ToBase64String(key));
        runner.Stored = Convert.ToBase64String(key);
        (await protector.UnprotectAsync(envelope, CancellationToken.None)).ShouldBe(key);
    }

    [Fact]
    public void SealedBoxRejectsWrongPasswordAndChangedHeader()
    {
        var magic = "TMCTEST1"u8.ToArray();
        var sealedData = SealedBox.Seal(magic, "pass", "hello"u8, KdfParameters.Fast);
        SealedBox.Open(magic, "pass", sealedData).ShouldBe("hello"u8.ToArray());
        Should.Throw<SealedBoxException>(() => SealedBox.Open(magic, "other", sealedData));
        var changed = (byte[])sealedData.Clone();
        changed[14] ^= 1;
        Should.Throw<SealedBoxException>(() => SealedBox.Open(magic, "pass", changed));
    }

    [Fact]
    public void ADerivedKeySealsWithoutThePasswordAndOpensWithIt()
    {
        var magic = "TMCTEST2"u8.ToArray();
        var key = SealedBox.Derive("backup password", KdfParameters.Fast);
        var first = SealedBox.Seal(magic, key, "one"u8);
        var second = SealedBox.Seal(magic, key, "two"u8);
        SealedBox.Open(magic, "backup password", first).ShouldBe("one"u8.ToArray());
        SealedBox.Open(magic, "backup password", second).ShouldBe("two"u8.ToArray());
        Should.Throw<SealedBoxException>(() => SealedBox.Open(magic, "another one", second));
    }
}
