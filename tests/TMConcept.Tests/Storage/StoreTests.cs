using Microsoft.EntityFrameworkCore;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Keys;
using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

public sealed class StoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<(StorageHarness H, Profile P)> WithProfileAsync()
    {
        var h = await StorageHarness.CreatedAsync();
        var profile = new Profile { Name = "Channel" };
        await new ProfileStore(h.Database).SaveAsync(profile, None);
        return (h, profile);
    }

    private static Concept New(Profile p, string title) => new() { ProfileId = p.Id, Title = title, Summary = "s", Content = "[x]\nc", Language = "en" };

    [Fact]
    public async Task NumbersIncreaseAndAreNeverReused()
    {
        var (h, p) = await WithProfileAsync();
        using var _ = h;
        var concepts = new ConceptStore(h.Database);
        var a = New(p, "A");
        var b = New(p, "B");
        await concepts.AddAsync([a, b], None);
        a.Number.ShouldBe(1);
        b.Number.ShouldBe(2);

        await concepts.SetStatusAsync([b.Id], ConceptStatus.Trashed, None);
        (await concepts.DeletePermanentlyAsync([b.Id], None)).ShouldBe(1);
        var c = New(p, "C");
        await concepts.AddAsync([c], None);
        c.Number.ShouldBe(3);
    }

    [Fact]
    public async Task PermanentDeleteRemovesOnlyTrashedRowsAndTheirEmbeddings()
    {
        var (h, p) = await WithProfileAsync();
        using var _ = h;
        var concepts = new ConceptStore(h.Database);
        var kept = New(p, "Kept");
        var trashed = New(p, "Trashed");
        await concepts.AddAsync([kept, trashed], None);
        await concepts.SetStatusAsync([trashed.Id], ConceptStatus.Trashed, None);
        await new EmbeddingStore(h.Database).SaveAsync([new ConceptEmbedding { ConceptId = trashed.Id, ProfileId = p.Id, Model = "m", Dimensions = 1, Vector = [0, 0, 128, 63] }], None);

        (await concepts.DeletePermanentlyAsync([kept.Id, trashed.Id], None)).ShouldBe(1);

        (await concepts.GetAsync(trashed.Id, None)).ShouldBeNull();
        (await concepts.GetAsync(kept.Id, None)).ShouldNotBeNull();
        (await concepts.HistoryAsync(p.Id, None)).Select(s => s.Title).ShouldBe(["Kept"]);
        (await new EmbeddingStore(h.Database).ListAsync(p.Id, "m", None)).ShouldBeEmpty();
        (await h.QueryAsync(db => db.Concepts.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task ListWithoutContentLeavesTheContentEmpty()
    {
        var (h, p) = await WithProfileAsync();
        using var _ = h;
        var concepts = new ConceptStore(h.Database);
        await concepts.AddAsync([New(p, "A")], None);
        var light = await concepts.ListAsync(p.Id, [ConceptStatus.New], withContent: false, None);
        light.Single().Content.ShouldBeEmpty();
        light.Single().Summary.ShouldBe("s");
        (await concepts.ListAsync(p.Id, [ConceptStatus.New], withContent: true, None)).Single().Content.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task DeletingAConceptKeepsItsVideoRecord()
    {
        var (h, p) = await WithProfileAsync();
        using var _ = h;
        var concepts = new ConceptStore(h.Database);
        var c = New(p, "Made");
        await concepts.AddAsync([c], None);
        var videos = new VideoStore(h.Database);
        var video = new VideoRecord { ConceptId = c.Id, ProfileId = p.Id, Title = "Made" };
        await videos.AddAsync(video, [new VideoCost { Step = "code", Usd = 0.1m, Exact = true }], None);

        await concepts.SetStatusAsync([c.Id], ConceptStatus.Trashed, None);
        await concepts.DeletePermanentlyAsync([c.Id], None);

        var kept = await videos.GetAsync(video.Id, None);
        kept.ShouldNotBeNull();
        kept.ConceptId.ShouldBeNull();
        (await videos.CostsAsync(video.Id, None)).Single().Usd.ShouldBe(0.1m);
    }

    [Fact]
    public async Task DeletingAProfileRemovesEverythingOfIt()
    {
        var (h, p) = await WithProfileAsync();
        using var _ = h;
        var concepts = new ConceptStore(h.Database);
        var c = New(p, "A");
        await concepts.AddAsync([c], None);
        await new QueueStore(h.Database).AddAsync(new QueueItem { ProfileId = p.Id, ConceptId = c.Id }, None);
        await new VideoStore(h.Database).AddAsync(new VideoRecord { ConceptId = c.Id, ProfileId = p.Id, Title = "A" }, [], None);
        await new ProfileStore(h.Database).SaveImageAsync(new ProfileImage { ProfileId = p.Id, FileName = "a.png" }, None);
        await new TopicSuggestionStore(h.Database).AddAsync([new TopicSuggestion { ProfileId = p.Id, Text = "Greek fire" }], None);
        await new ConceptWishStore(h.Database).AddAsync(new ConceptWish { ProfileId = p.Id, Text = "Shorter sentences" }, None);

        await new ProfileStore(h.Database).DeleteAsync(p.Id, None);

        (await h.QueryAsync(db => db.Concepts.CountAsync())).ShouldBe(0);
        (await h.QueryAsync(db => db.QueueItems.CountAsync())).ShouldBe(0);
        (await h.QueryAsync(db => db.Videos.CountAsync())).ShouldBe(0);
        (await h.QueryAsync(db => db.ProfileImages.CountAsync())).ShouldBe(0);
        (await h.QueryAsync(db => db.TopicSuggestions.CountAsync())).ShouldBe(0);
        (await h.QueryAsync(db => db.ConceptWishes.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task OnlyTheNewestWishesOfAProfileAreKeptAndARepeatMovesToTheFront()
    {
        var (h, p) = await WithProfileAsync();
        using var _ = h;
        var store = new ConceptWishStore(h.Database);
        var at = DateTimeOffset.UtcNow;
        for (var i = 1; i <= PromptMemoryLimits.WishesKept + 3; i++)
        {
            await store.AddAsync(new ConceptWish { ProfileId = p.Id, Text = "Wish " + i, CreatedAtUtc = at.AddMinutes(i) }, None);
        }

        var kept = await store.ListAsync(p.Id, 100, None);
        kept.Count.ShouldBe(PromptMemoryLimits.WishesKept);
        kept[0].Text.ShouldBe("Wish 11");
        kept[^1].Text.ShouldBe("Wish 4");

        await store.AddAsync(new ConceptWish { ProfileId = p.Id, Text = "Wish 5", CreatedAtUtc = at.AddMinutes(20) }, None);
        kept = await store.ListAsync(p.Id, 100, None);
        kept.Count.ShouldBe(PromptMemoryLimits.WishesKept, "the same wish again is one wish");
        kept[0].Text.ShouldBe("Wish 5");
        kept.Count(w => w.Text == "Wish 5").ShouldBe(1);
        (await store.ListAsync(Guid.NewGuid(), 100, None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ATrashReasonIsKeptAndClearedWhenTheConceptLeavesTheTrash()
    {
        var (h, p) = await WithProfileAsync();
        using var _ = h;
        var concepts = new ConceptStore(h.Database);
        var c = New(p, "A");
        await concepts.AddAsync([c], None);

        await concepts.TrashAsync([c.Id], TrashReason.Topic, None);
        var trashed = (await concepts.ListAsync(p.Id, [ConceptStatus.Trashed], false, None)).Single();
        (trashed.TrashReason, trashed.DecidedAtUtc.HasValue).ShouldBe((TrashReason.Topic, true));

        await concepts.SetStatusAsync([c.Id], ConceptStatus.New, None);
        (await concepts.GetAsync(c.Id, None))!.TrashReason.ShouldBe(TrashReason.None, "restored: no dislike any more");

        await concepts.SetStatusAsync([c.Id], ConceptStatus.Trashed, None);
        (await concepts.GetAsync(c.Id, None))!.TrashReason.ShouldBe(TrashReason.None, "trashed without a reason (the Delete key)");
    }

    [Fact]
    public async Task TopicSuggestionsAreListedNewestFirstAndByStatusInSelectionOrder()
    {
        var (h, p) = await WithProfileAsync();
        using var _ = h;
        var store = new TopicSuggestionStore(h.Database);
        var at = DateTimeOffset.UtcNow;
        await store.AddAsync([
            new TopicSuggestion { ProfileId = p.Id, Text = "Old", CreatedAtUtc = at.AddMinutes(-2) },
            new TopicSuggestion { ProfileId = p.Id, Text = "New", CreatedAtUtc = at },
        ], None);

        (await store.ListAsync(p.Id, 10, None)).Select(s => s.Text).ShouldBe(["New", "Old"]);
        (await store.ListAsync(p.Id, 1, None)).Select(s => s.Text).ShouldBe(["New"]);
        (await store.ListAsync(Guid.NewGuid(), 10, None)).ShouldBeEmpty();

        (await store.ListAsync(p.Id, 10, None)).ShouldAllBe(s => s.Status == TopicStatus.Offered, "a new suggestion is only offered");

        var all = await store.ListAsync(p.Id, 10, None);
        var press = Enumerable.Range(0, 3).Select(i => new TopicSuggestion { ProfileId = p.Id, Text = "Press " + i, CreatedAtUtc = at.AddMinutes(1).AddTicks(i) })
            .ToList();
        await store.AddAsync(press, None);
        await store.SetStatusAsync([press[2].Id, press[0].Id, all.Single(s => s.Text == "Old").Id, Guid.NewGuid()], TopicStatus.Selected, None);
        (await store.ListAsync(p.Id, TopicStatus.Selected, None)).Select(s => s.Text).ShouldBe(["Old", "Press 0", "Press 2"],
            "oldest first: the order they were suggested in; an unknown id is skipped");
        (await store.ListAsync(p.Id, TopicStatus.Offered, None)).Select(s => s.Text).ShouldBe(["New", "Press 1"]);

        await store.SetStatusAsync([press[0].Id], TopicStatus.Made, None);
        await store.SetStatusAsync([], TopicStatus.Made, None);
        (await store.ListAsync(p.Id, TopicStatus.Selected, None)).Select(s => s.Text).ShouldBe(["Old", "Press 2"]);
        (await store.ListAsync(p.Id, TopicStatus.Made, None)).Select(s => s.Text).ShouldBe(["Press 0"]);
        (await store.ListAsync(Guid.NewGuid(), TopicStatus.Selected, None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task QueueKeepsItsOrder()
    {
        var (h, p) = await WithProfileAsync();
        using var _ = h;
        var concepts = new ConceptStore(h.Database);
        var a = New(p, "A");
        var b = New(p, "B");
        await concepts.AddAsync([a, b], None);
        var queue = new QueueStore(h.Database);
        var qa = new QueueItem { ProfileId = p.Id, ConceptId = a.Id };
        var qb = new QueueItem { ProfileId = p.Id, ConceptId = b.Id };
        await queue.AddAsync(qa, None);
        await queue.AddAsync(qb, None);
        await queue.ReorderAsync(p.Id, [qb.Id, qa.Id], None);
        (await queue.ListAsync(p.Id, None)).Select(q => q.ConceptId).ShouldBe([b.Id, a.Id]);
    }

    [Fact]
    public async Task ApiKeysRoundTrip()
    {
        using var h = await StorageHarness.CreatedAsync();
        var keys = new ApiKeyStore(h.Database);
        var key = new ApiKey { Provider = "anthropic", Label = "personal", Secret = "value-1" };
        await keys.SaveAsync(key, None);
        await keys.MarkOkAsync(key.Id, DateTimeOffset.UtcNow, None);
        var read = (await keys.ListAsync("anthropic", None)).Single();
        read.Secret.ShouldBe("value-1");
        read.LastOkAtUtc.ShouldNotBeNull();
    }
}
