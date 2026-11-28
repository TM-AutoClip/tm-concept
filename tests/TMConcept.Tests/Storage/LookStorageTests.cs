using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Storage.Database;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>
/// 28 Nov 2026: the profile's look (drawing style, motion, finishing layer) is kept with its step choices (no table of its own), and
/// concepts are written for no style any more: the schema step <c>Version0158</c> drops <c>concepts.DrawStyle</c> and keeps the concepts.
/// </summary>
public sealed class LookStorageTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task AProfilesLookIsKeptPerProfileAndADamagedOneReadsAsTheDefault()
    {
        using var h = await StorageHarness.CreatedAsync();
        var one = new Profile { Name = "One" };
        var two = new Profile { Name = "Two" };
        await new ProfileStore(h.Database).SaveAsync(one, None);
        await new ProfileStore(h.Database).SaveAsync(two, None);
        var steps = new StepModelStore(h.Database);
        var store = new VideoLookStore(steps);

        var empty = await store.GetAsync(one.Id, None);
        (empty.DrawStyle, empty.Motion, empty.Finish).ShouldBe((DrawStyle.Auto, MotionIntensity.Normal, true), "no choice: the AI chooses");

        await store.SaveAsync(one.Id, new VideoLook { DrawStyle = DrawStyle.StickFigure, Motion = MotionIntensity.Lively, Finish = false }, None);
        var back = await store.GetAsync(one.Id, None);
        (back.DrawStyle, back.Motion, back.Finish).ShouldBe((DrawStyle.StickFigure, MotionIntensity.Lively, false));
        (await store.GetAsync(two.Id, None)).DrawStyle.ShouldBe(DrawStyle.Auto, "another profile keeps its own");
        var row = (await steps.GetAsync(PipelineSteps.VideoLook, one.Id, None)).ShouldNotBeNull();
        row.Provider.ShouldBe(VideoLookStore.Provider, "no catalog model: nothing installs or prices it");
        (await steps.GetAsync(PipelineSteps.VideoMedia, one.Id, None)).ShouldBeNull("the music choice is a row of its own");

        foreach (var damaged in new[] { "{not json", "{\"drawStyle\":99}", "{\"motion\":-4}" })
        {
            await steps.SaveAsync(new StepModelChoice { Step = PipelineSteps.VideoLook, ProfileId = two.Id, Provider = "app", Model = "look", OptionsJson = damaged }, None);
            (await store.GetAsync(two.Id, None)).DrawStyle.ShouldBe(DrawStyle.Auto, damaged);
        }
    }

    [Fact]
    public async Task AFileOf0132IsUpgradedWithoutTheConceptsDrawStyleAndKeepsItsConcepts()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = new Profile { Name = "Old channel", ContentLanguage = ContentLanguages.Turkish };
        var concept = new Concept { ProfileId = profile.Id, Title = "Mohács", Summary = "s", Content = "c", Language = ContentLanguages.Turkish };

        // The file as 0.1.32 to 0.1.57 left it: every step up to Version0132, with a profile, a concept and the rows that point at it.
        var steps = await h.QueryAsync(async db =>
        {
            var all = db.Database.GetMigrations().ToList();
            all.Select(SchemaUpgrader.Name).TakeLast(2).ShouldBe(["Version0132", "Version0158"]);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("0", None);
            await migrator.MigrateAsync(all[^2], None);
            return all;
        });
        await new ProfileStore(h.Database).SaveAsync(profile, None);
        await new ConceptStore(h.Database).AddAsync([concept], None);
        var item = new QueueItem { ProfileId = profile.Id, ConceptId = concept.Id };
        await new QueueStore(h.Database).AddAsync(item, None);
        var video = new VideoRecord { ProfileId = profile.Id, ConceptId = concept.Id, Title = "Mohács", Folder = "Old channel/Mohács/v1", FileName = "video.mp4" };
        await new VideoStore(h.Database).AddAsync(video, [], None);
        await new VideoRatingStore(h.Database).SaveAsync(
            new VideoRating { ProfileId = profile.Id, ConceptId = concept.Id, VideoGroup = "Old channel/Mohács", Vote = VideoVote.Like, Title = "Mohács", Summary = "s" }, None);
        await new ConceptWishStore(h.Database).AddAsync(new ConceptWish { ProfileId = profile.Id, Text = "Shorter sentences" }, None);
        await new EmbeddingStore(h.Database).SaveAsync(
            [new ConceptEmbedding { ConceptId = concept.Id, ProfileId = profile.Id, Model = "m", Dimensions = 1, Vector = [0, 0, 128, 63] }], None);
        await using (var inspect = InspectionContext.Open(h.ConnectionString))
        {
            (await inspect.Master.Where(m => m.Name == "concepts").Select(m => m.Sql).SingleAsync(None)).ShouldNotBeNull().ShouldContain("\"DrawStyle\"");
        }

        h.Vault.Lock();
        await h.Vault.OpenAsync(None);

        (await h.QueryAsync(async db => (await db.Database.GetAppliedMigrationsAsync(None)).ToList())).ShouldBe(steps);
        await using (var inspect = InspectionContext.Open(h.ConnectionString))
        {
            (await inspect.Master.Where(m => m.Name == "concepts").Select(m => m.Sql).SingleAsync(None)).ShouldNotBeNull().ShouldNotContain("DrawStyle");
        }

        (await new ConceptStore(h.Database).GetAsync(concept.Id, None)).ShouldNotBeNull().Title.ShouldBe("Mohács", "the concept is kept");

        // The step rebuilds the concepts table (SQLite drops a column that way): nothing that points at it is deleted or cut loose.
        (await new QueueStore(h.Database).ListAsync(profile.Id, None)).ShouldHaveSingleItem().ConceptId.ShouldBe(concept.Id, "the queue item is kept");
        (await new VideoStore(h.Database).GetAsync(video.Id, None)).ShouldNotBeNull().ConceptId.ShouldBe(concept.Id, "the video keeps its concept");
        (await new VideoRatingStore(h.Database).ListAsync(profile.Id, None)).ShouldHaveSingleItem().ConceptId.ShouldBe(concept.Id, "the rating keeps its concept");
        (await new ConceptWishStore(h.Database).ListAsync(profile.Id, 10, None)).ShouldHaveSingleItem().Text.ShouldBe("Shorter sentences");
        (await new EmbeddingStore(h.Database).ListAsync(profile.Id, "m", None)).ShouldHaveSingleItem().ConceptId.ShouldBe(concept.Id, "the vectors are kept");
        // The foreign keys are enforced again after the rebuild: a queue item of a concept that does not exist is refused.
        await Should.ThrowAsync<DbUpdateException>(
            () => new QueueStore(h.Database).AddAsync(new QueueItem { ProfileId = profile.Id, ConceptId = Guid.NewGuid() }, None));
        (await new QueueStore(h.Database).ListAsync(profile.Id, None)).ShouldHaveSingleItem("the refused item is not added");
    }
}
