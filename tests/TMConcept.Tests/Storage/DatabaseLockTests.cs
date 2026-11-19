using Microsoft.EntityFrameworkCore;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Storage.Database;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>
/// "database is locked" (seen on 0.1.28): when a concept batch was saved, the picked topics were marked while the run's final update was
/// written, and the update failed at once (the run stayed "running", a saved run was reported as failed). Every connection now waits for
/// another connection's lock.
/// </summary>
public sealed class DatabaseLockTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task AWriteWaitsForAnotherConnectionsWriteLockLongerThanTheDriversOwnRetry()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = new Profile { Name = "Channel" };
        await new ProfileStore(h.Database).SaveAsync(profile, None);

        // Another piece of work holds the write lock for 2.5 s (a transaction is BEGIN IMMEDIATE).
        await using var other = await ConceptDatabase.OpenAsync(h.ConnectionString, None);
        var holding = await other.Database.BeginTransactionAsync(None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(2500, None);
            await holding.CommitAsync(None);
            await holding.DisposeAsync();
        }, None);

        // Microsoft.Data.Sqlite's own retry of a busy statement gives up after the command timeout (1 s here), so the write only goes
        // through when the connection itself waits for the lock (WaitForLocks; without it this throws "database is locked").
        await using var db = await ConceptDatabase.OpenAsync(h.ConnectionString, None);
        db.Database.SetCommandTimeout(1);
        await Should.NotThrowAsync(
            () => db.Profiles.Where(p => p.Id == profile.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "Renamed"), None),
            "the write waits for the other connection's lock");
        await release;

        (await new ProfileStore(h.Database).GetAsync(profile.Id, None)).ShouldNotBeNull().Name.ShouldBe("Renamed");
    }

    [Fact]
    public async Task ConcurrentWritesAndReadsAllGoThrough()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = new Profile { Name = "Channel" };
        await new ProfileStore(h.Database).SaveAsync(profile, None);
        var concepts = new ConceptStore(h.Database);
        await concepts.AddAsync(
            [.. Enumerable.Range(0, 20).Select(i => new Concept { ProfileId = profile.Id, Title = "T" + i, Summary = "s", Content = new string('c', 2000), Language = "en" })], None);
        var runs = new RunStore(h.Database);
        var run = new Run { ProfileId = profile.Id, Kind = RunKind.Concepts };
        await runs.AddAsync(run, None);
        var topics = new TopicSuggestionStore(h.Database);
        var suggestions = Enumerable.Range(0, 10).Select(i => new TopicSuggestion { ProfileId = profile.Id, Text = "Topic " + i }).ToList();
        await topics.AddAsync(suggestions, None);
        const int Rounds = 25;

        // The same three pieces of work as when a batch is saved: marking the picked topics, the run's update, a page reading the concepts.
        await Should.NotThrowAsync(() => Task.WhenAll(
            Task.Run(async () =>
            {
                for (var i = 0; i < Rounds; i++)
                    await topics.MarkPickedAsync([suggestions[i % suggestions.Count].Id], None);
            }, None),
            Task.Run(async () =>
            {
                for (var i = 1; i <= Rounds; i++)
                    await runs.UpdateAsync(new Run { Id = run.Id, ProfileId = profile.Id, Kind = RunKind.Concepts, Accepted = i }, None);
            }, None),
            Task.Run(async () =>
            {
                for (var i = 0; i < Rounds; i++)
                    (await concepts.ListAsync(profile.Id, [ConceptStatus.New], withContent: true, None)).Count.ShouldBe(20);
            }, None)));

        (await runs.GetAsync(run.Id, None)).ShouldNotBeNull().Accepted.ShouldBe(Rounds, "the last update is the one kept");
    }
}
