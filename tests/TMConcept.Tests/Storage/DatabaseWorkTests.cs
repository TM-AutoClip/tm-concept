using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Profiles;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Keys;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>
/// 0.1.39: every piece of database work runs on the thread pool, writes go one at a time, connections are pooled and the pool lets the
/// file go before it is replaced, moved or locked.
/// </summary>
public sealed class DatabaseWorkTests
{
    private const string BackupPassword = "backup password 123";
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public void ConnectionsArePooled()
    {
        new SqliteConnectionStringBuilder(ConceptDatabase.ConnectionString("x.db", "00")).Pooling.ShouldBeTrue();
    }

    [Fact]
    public async Task WorkNeverRunsOnTheCallersContext()
    {
        using var h = await StorageHarness.CreatedAsync();
        var previous = SynchronizationContext.Current;
        var window = new MarkedContext();
        SynchronizationContext.SetSynchronizationContext(window);
        try
        {
            // The calling thread stays busy until both pieces of work have run, so the pool cannot hand it the work after it returns.
            var caller = Environment.CurrentManagedThreadId;
            using var ran = new CountdownEvent(2);
            (SynchronizationContext? Context, int Thread) Seen()
            {
                var seen = (SynchronizationContext.Current, Environment.CurrentManagedThreadId);
                ran.Signal();
                return seen;
            }

            var read = h.Database.ReadAsync(db => Task.FromResult(Seen()), None);
            var write = h.Database.WriteAsync(db => Task.FromResult(Seen()), None);
            ran.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken).ShouldBeTrue("both pieces of work ran while the caller waited");
            foreach (var (context, thread) in await Task.WhenAll(read, write))
            {
                context.ShouldNotBeSameAs(window, "the work does not see the window's context");
                thread.ShouldNotBe(caller, "the work does not run on the calling thread");
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task UnlockRunsOffTheCallersThread()
    {
        using var h = await StorageHarness.CreatedAsync();
        h.Vault.Lock();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new MarkedContext());
        try
        {
            // The key callback runs inside the unlock: the master password's Argon2id and the file's key derivation run where it runs.
            var caller = Environment.CurrentManagedThreadId;
            using var opened = new ManualResetEventSlim();
            var thread = 0;
            var open = h.Vault.OpenAsync(() =>
            {
                thread = Environment.CurrentManagedThreadId;
                opened.Set();
            }, None);
            opened.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken).ShouldBeTrue("the key was opened while the caller waited");
            await open;
            thread.ShouldNotBe(caller, "unsealing the key does not hold the calling (window's) thread");
            h.Vault.State.ShouldBe(VaultState.Open);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task TwentyWritesAtOnceAllGoThroughOneAtATime()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = new Profile { Name = "Channel" };
        await new ProfileStore(h.Database).SaveAsync(profile, None);
        var store = new ConceptStore(h.Database);

        // Each save reads the profile's counter and then writes (the read-then-write that ends in "database is locked" when two run at once).
        await Should.NotThrowAsync(() => Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => store.AddAsync(
            [new Concept { ProfileId = profile.Id, Title = "T" + i, Summary = "s", Content = "c", Language = "en" }], None), None))));

        var all = await store.ListAsync(profile.Id, [ConceptStatus.New], withContent: false, None);
        all.Count.ShouldBe(20);
        all.Select(c => c.Number).Order().ShouldBe(Enumerable.Range(1, 20), "every save got its own number");
    }

    [Fact]
    public async Task WritesTakeTurnsWhileReadsRunSideBySide()
    {
        using var h = await StorageHarness.CreatedAsync();
        var writing = 0;
        var mostWriting = 0;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => h.Database.WriteAsync(async db =>
        {
            var now = Interlocked.Increment(ref writing);
            InterlockedMax(ref mostWriting, now);
            await Task.Delay(20, None);
            Interlocked.Decrement(ref writing);
        }, None)));
        mostWriting.ShouldBe(1);

        // Two reads wait for each other: they only both finish when they run at the same time.
        using var bothIn = new CountdownEvent(2);
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => h.Database.ReadAsync(db =>
        {
            bothIn.Signal();
            bothIn.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the other read runs at the same time");
            return Task.CompletedTask;
        }, None)));
    }

    [Fact]
    public async Task RestoredDataIsRightAndTheFileIsNotHeld()
    {
        using var source = await StorageHarness.CreatedAsync();
        await new ProfileStore(source.Database).SaveAsync(new Profile { Name = "Kept channel" }, None);
        var backup = Service(source);
        await backup.SetPasswordAsync(BackupPassword, None);
        var file = Path.Combine(source.Paths.DataDirectory, "manual" + BackupRules.Extension);
        await backup.CreateAsync(file, BackupContents.Automatic, None);

        using var target = new StorageHarness();
        await Service(target).RestoreAsync(file, BackupPassword, target.Protector, None);
        (await new ProfileStore(target.Database).ListAsync(None)).Single().Name.ShouldBe("Kept channel");

        // The pooled connections of the restored file let it go: it can be moved away and back.
        target.Vault.Lock();
        DatabaseWork.ReleaseFiles(target.Paths.DatabaseFile).ShouldBeTrue();
        var moved = target.Paths.DatabaseFile + ".moved";
        File.Move(target.Paths.DatabaseFile, moved);
        File.Move(moved, target.Paths.DatabaseFile);
        await target.Vault.OpenAsync(None);
        (await new ProfileStore(target.Database).ListAsync(None)).Single().Name.ShouldBe("Kept channel");
    }

    [Fact]
    public async Task AfterAPasswordChangeTheOldPasswordOpensNothing()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profiles = new ProfileStore(h.Database);
        await profiles.SaveAsync(new Profile { Name = "Channel" }, None);
        (await profiles.ListAsync(None)).Count.ShouldBe(1, "a pooled connection is open now");

        var old = h.Passwords.Password;
        h.Passwords.Password = "a brand new password";
        await h.Vault.ChangeProtectionAsync(h.Protector, None);
        h.Vault.Lock();

        // Locked: no pooled connection answers without the key.
        await Should.ThrowAsync<InvalidOperationException>(() => profiles.ListAsync(None));
        h.Passwords.Password = old;
        var opened = true;
        try
        {
            await h.Vault.OpenAsync(None);
        }
        catch (Exception)
        {
            opened = false;
        }

        opened.ShouldBeFalse("the old password does not unseal the key");
        h.Vault.State.ShouldBe(VaultState.Locked);

        h.Passwords.Password = "a brand new password";
        await h.Vault.OpenAsync(None);
        (await profiles.ListAsync(None)).Single().Name.ShouldBe("Channel");
    }

    [Fact]
    public async Task AnotherKeyNeverGetsAPooledConnection()
    {
        using var h = await StorageHarness.CreatedAsync();
        (await h.QueryAsync(db => db.Profiles.CountAsync(None))).ShouldBe(0);
        var wrong = ConceptDatabase.ConnectionString(h.Paths.DatabaseFile, new string('A', 64));
        await Should.ThrowAsync<SqliteException>(() => StorageHarness.QueryAsync(wrong, db => db.Profiles.CountAsync(None)));
    }

    private static BackupService Service(StorageHarness h) =>
        new(h.Paths, h.Key, h.Vault, h.Database, KdfParameters.Fast, TimeProvider.System, NullLogger<BackupService>.Instance);

    private static void InterlockedMax(ref int target, int value)
    {
        var seen = Volatile.Read(ref target);
        while (value > seen)
        {
            var was = Interlocked.CompareExchange(ref target, value, seen);
            if (was == seen)
            {
                return;
            }

            seen = was;
        }
    }
}
