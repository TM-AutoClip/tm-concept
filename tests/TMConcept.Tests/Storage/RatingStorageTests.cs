using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>
/// 0.1.18: the video ratings (<c>video_ratings</c>) and the picked mark of the topic suggestions are stored, outlive the concept they were
/// made from, go into a backup with the concepts and come back, and a 0.1.17 file with data is upgraded to them.
/// </summary>
public sealed class RatingStorageTests
{
    private const string Password = "backup password 123";

    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task ARatingIsKeptReplacedAndOutlivesItsConceptAndItsVideo()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = new Profile { Name = "Channel" };
        await new ProfileStore(h.Database).SaveAsync(profile, None);
        var concepts = new ConceptStore(h.Database);
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "A secret weapon. How it burned on water.", Content = "c", Language = "en" };
        await concepts.AddAsync([concept], None);
        var video = new VideoRecord { ProfileId = profile.Id, ConceptId = concept.Id, Title = "Greek fire", Folder = "Channel/Greek fire/v1", FileName = "video.mp4" };
        await new VideoStore(h.Database).AddAsync(video, [], None);
        var ratings = new VideoRatingStore(h.Database);
        var rater = new VideoRater(ratings, concepts, TimeProvider.System, NullLogger<VideoRater>.Instance);

        (await rater.RateAsync(video, VideoVote.Dislike, None)).ShouldBe(VideoVote.Dislike);
        (await rater.RateAsync(video, VideoVote.Like, None)).ShouldBe(VideoVote.Like, "the other button changes the vote");
        var stored = (await ratings.ListAsync(profile.Id, None)).ShouldHaveSingleItem("one rating per video");
        stored.Vote.ShouldBe(VideoVote.Like);
        stored.Summary.ShouldBe(concept.Summary);
        stored.VideoGroup.ShouldBe("Channel/Greek fire");

        await new VideoStore(h.Database).DeleteAsync(video.Id, None);
        await concepts.SetStatusAsync([concept.Id], ConceptStatus.Trashed, None);
        await concepts.DeletePermanentlyAsync([concept.Id], None);

        var left = (await ratings.ListAsync(profile.Id, None)).ShouldHaveSingleItem("the copies keep steering the prompts");
        left.ConceptId.ShouldBeNull();
        left.Title.ShouldBe("Greek fire");
        left.Summary.ShouldBe(concept.Summary);

        await new ProfileStore(h.Database).DeleteAsync(profile.Id, None);
        (await h.QueryAsync(db => db.VideoRatings.CountAsync())).ShouldBe(0, "deleted with the profile");
    }

    [Fact]
    public async Task RatingsAndPickedTopicsGoWithTheConceptsIntoABackupAndComeBack()
    {
        using var source = await StorageHarness.CreatedAsync();
        var profile = new Profile { Name = "Kept channel" };
        await new ProfileStore(source.Database).SaveAsync(profile, None);
        var picked = new TopicSuggestion { ProfileId = profile.Id, Text = "Greek fire" };
        var shown = new TopicSuggestion { ProfileId = profile.Id, Text = "The Varangian Guard" };
        var suggestions = new TopicSuggestionStore(source.Database);
        await suggestions.AddAsync([picked, shown], None);
        await suggestions.MarkPickedAsync([picked.Id], None);
        await new VideoRatingStore(source.Database).SaveAsync(new VideoRating
        {
            ProfileId = profile.Id, VideoGroup = "Kept channel/Greek fire", Title = "Greek fire", Summary = "s", Vote = VideoVote.Dislike,
        }, None);
        var service = Service(source);
        await service.SetPasswordAsync(Password, None);
        var all = Path.Combine(source.Paths.DataDirectory, "all" + BackupRules.Extension);
        var noConcepts = Path.Combine(source.Paths.DataDirectory, "no-concepts" + BackupRules.Extension);
        await service.CreateAsync(all, BackupContents.Automatic, None);
        await service.CreateAsync(noConcepts, new BackupContents { Concepts = false }, None);

        using (var target = new StorageHarness())
        {
            await Service(target).RestoreAsync(all, Password, target.Protector, None);
            var rating = (await new VideoRatingStore(target.Database).ListAsync(profile.Id, None)).ShouldHaveSingleItem();
            rating.Vote.ShouldBe(VideoVote.Dislike);
            rating.Title.ShouldBe("Greek fire");
            var back = await new TopicSuggestionStore(target.Database).ListAsync(profile.Id, 10, None);
            back.Single(s => s.Id == picked.Id).Outcome.ShouldBe(TopicOutcome.Picked);
            back.Single(s => s.Id == shown.Id).Outcome.ShouldBe(TopicOutcome.Shown);
        }

        using (var target = new StorageHarness())
        {
            await Service(target).RestoreAsync(noConcepts, Password, target.Protector, None);
            (await new ProfileStore(target.Database).ListAsync(None)).ShouldNotBeEmpty();
            (await new VideoRatingStore(target.Database).ListAsync(profile.Id, None)).ShouldBeEmpty("ratings go with the concepts");
        }
    }

    [Fact]
    public async Task AFileOf0117WithDataIsUpgradedAndItsSuggestionsStayBlocked()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = new Profile { Name = "Old channel" };
        var concept = new Concept { Title = "Hagia Sophia", Summary = "s", Content = "c", Language = "en" };
        var suggestionId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

        // The file as 0.1.17 left it: only its own step, then its data (the tables that did not change through the app's stores).
        var first = await h.QueryAsync(async db =>
        {
            var steps = db.Database.GetMigrations().ToList();
            steps.Select(SchemaUpgrader.Name).ShouldContain("Version0118");
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("0", None);
            await migrator.MigrateAsync(steps[0], None);
            return steps[0];
        });
        SchemaUpgrader.Name(first).ShouldBe("Version0117");

        // The profile as that version wrote it (the app's mapping has the channel photo of 0.1.19 since).
        await using (var old = Version0118Context.Open(h.ConnectionString))
        {
            old.Profiles.Add(new OldProfileRow
            {
                Id = profile.Id.ToString("D").ToUpperInvariant(), Name = profile.Name, ContentLanguage = profile.ContentLanguage, TargetSeconds = 75,
                ConceptText = string.Empty, ConceptCounter = 1, CreatedAtUtc = at.UtcTicks, UpdatedAtUtc = at.UtcTicks,
            });
            await old.SaveChangesAsync(None);
            concept.ProfileId = profile.Id;
            old.Concepts.Add(OldConceptRow.Of(concept, 1));
            await old.SaveChangesAsync(None);
        }

        await new VideoStore(h.Database).AddAsync(new VideoRecord
        {
            ProfileId = profile.Id, ConceptId = concept.Id, Title = "Hagia Sophia", Folder = "Old channel/Hagia Sophia/v1",
        }, [], None);
        await using (var old = Version0117Context.Open(h.ConnectionString))
        {
            old.TopicSuggestions.Add(new OldSuggestionRow
            {
                Id = suggestionId.ToString("D").ToUpperInvariant(), ProfileId = profile.Id.ToString("D").ToUpperInvariant(), Text = "Greek fire",
                CreatedAtUtc = at.UtcTicks,
            });
            await old.SaveChangesAsync(None);
        }

        h.Vault.Lock();
        await h.Vault.OpenAsync(None);

        (await h.QueryAsync(async db => (await db.Database.GetAppliedMigrationsAsync(None)).Select(SchemaUpgrader.Name).ToList()))
            .ShouldBe(["Version0117", "Version0118", "Version0119", "Version0120"]);
        var suggestion = (await new TopicSuggestionStore(h.Database).ListAsync(profile.Id, 10, None)).ShouldHaveSingleItem();
        suggestion.Id.ShouldBe(suggestionId);
        suggestion.Text.ShouldBe("Greek fire");
        suggestion.CreatedAtUtc.ShouldBe(at);
        suggestion.Outcome.ShouldBe(TopicOutcome.Picked,
            "0.1.17 blocked every suggestion and never recorded which ones got a concept: they stay blocked (a topic with a concept never comes back)");
        var fresh = new TopicSuggestion { ProfileId = profile.Id, Text = "Varangian Guard" };
        await new TopicSuggestionStore(h.Database).AddAsync([fresh], None);
        (await new TopicSuggestionStore(h.Database).ListAsync(profile.Id, 10, None)).Single(s => s.Id == fresh.Id).Outcome
            .ShouldBe(TopicOutcome.Shown, "a suggestion made after the upgrade is only shown: it may come again");
        (await new ConceptStore(h.Database).GetAsync(concept.Id, None)).ShouldNotBeNull().Title.ShouldBe("Hagia Sophia");
        var video = (await new VideoStore(h.Database).ListAsync(profile.Id, None)).ShouldHaveSingleItem();
        (await new VideoRatingStore(h.Database).ListAsync(profile.Id, None)).ShouldBeEmpty("nothing is rated by the upgrade");

        var rater = new VideoRater(new VideoRatingStore(h.Database), new ConceptStore(h.Database), TimeProvider.System, NullLogger<VideoRater>.Instance);
        (await rater.RateAsync(video, VideoVote.Like, None)).ShouldBe(VideoVote.Like);
        (await new VideoRatingStore(h.Database).ListAsync(profile.Id, None)).ShouldHaveSingleItem().ConceptId.ShouldBe(concept.Id);
    }

    private static BackupService Service(StorageHarness h) =>
        new(h.Paths, h.Key, h.Vault, h.Database, KdfParameters.Fast, TimeProvider.System, NullLogger<BackupService>.Instance);
}
