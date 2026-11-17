using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Styles;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>
/// 0.1.19: the templates (<c>video_styles</c>, at most 20, global) and the profile's channel photo (<c>profiles.ChannelPhoto</c>) are
/// stored, go into a backup (templates with the settings, the photo file with the profiles) and come back, and a 0.1.18 file with data is
/// upgraded to them.
/// </summary>
public sealed class StyleStorageTests
{
    private const string Password = "backup password 123";

    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task ATemplateIsAddedReadChangedAndDeletedCompletelyWithEveryField()
    {
        using var h = await StorageHarness.CreatedAsync();
        var store = new VideoStyleStore(h.Database);
        var created = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
        var style = new VideoStyle
        {
            Name = "  Shorts  ", Subtitles = false, ProgressBar = true, FontId = "anton", TitleStyle = TitleStyle.Boxed,
            TitlePosition = TitlePosition.BottomRight, PaletteId = "paper", DrawStyle = DrawStyle.Neon, Motion = MotionIntensity.Lively,
            Closing = ClosingPlacement.Both, ClosingPhrase = ClosingPhrase.Thanks, ClosingPlatform = ClosingPlatform.TikTok,
            CreatedAtUtc = created, UpdatedAtUtc = created,
        };
        await store.SaveAsync(style, None);
        await store.SaveAsync(new VideoStyle { Name = "Calm history" }, None);

        (await store.ListAsync(None)).Select(s => s.Name).ShouldBe(["Calm history", "Shorts"], "by name, the name trimmed");
        var back = (await store.GetAsync(style.Id, None)).ShouldNotBeNull();
        (back.Subtitles, back.ProgressBar, back.FontId, back.TitleStyle, back.TitlePosition, back.PaletteId, back.DrawStyle, back.Motion)
            .ShouldBe((false, true, "anton", TitleStyle.Boxed, TitlePosition.BottomRight, "paper", DrawStyle.Neon, MotionIntensity.Lively));
        (back.Closing, back.ClosingPhrase, back.ClosingPlatform, back.CreatedAtUtc).ShouldBe((ClosingPlacement.Both, ClosingPhrase.Thanks, ClosingPlatform.TikTok, created));

        back.Name = "Shorts 2";
        back.ProgressBar = false;
        await store.SaveAsync(back, None);
        var changed = (await store.GetAsync(style.Id, None)).ShouldNotBeNull();
        (changed.Name, changed.ProgressBar).ShouldBe(("Shorts 2", false));
        (await store.ListAsync(None)).Count.ShouldBe(2, "a change is not a new template");

        await store.DeleteAsync(style.Id, None);
        (await store.GetAsync(style.Id, None)).ShouldBeNull();
        (await h.QueryAsync(db => db.VideoStyles.CountAsync())).ShouldBe(1, "deleted completely, no trash");
        await store.DeleteAsync(Guid.NewGuid(), None);
    }

    [Fact]
    public async Task AtMostTwentyTemplatesAndAWrongOneIsNeverWritten()
    {
        using var h = await StorageHarness.CreatedAsync();
        var store = new VideoStyleStore(h.Database);
        var styles = Enumerable.Range(1, VideoStyleLimits.Max).Select(i => new VideoStyle { Name = "Template " + i }).ToList();
        foreach (var style in styles)
        {
            await store.SaveAsync(style, None);
        }

        await Should.ThrowAsync<VideoStyleLimitException>(() => store.SaveAsync(new VideoStyle { Name = "One too many" }, None));
        (await store.ListAsync(None)).Count.ShouldBe(VideoStyleLimits.Max);

        // At the limit a stored one can still be changed, and after a delete a new one fits again.
        styles[0].Motion = MotionIntensity.Calm;
        await store.SaveAsync(styles[0], None);
        await store.DeleteAsync(styles[1].Id, None);
        await store.SaveAsync(new VideoStyle { Name = "Fits again" }, None);
        (await store.ListAsync(None)).Count.ShouldBe(VideoStyleLimits.Max);

        await Should.ThrowAsync<ArgumentException>(() => store.SaveAsync(new VideoStyle { Name = " " }, None));
        await Should.ThrowAsync<ArgumentException>(() => store.SaveAsync(new VideoStyle { Name = "x", PaletteId = "no-such" }, None));
        await Should.ThrowAsync<ArgumentException>(() => store.SaveAsync(new VideoStyle { Name = new string('x', VideoStyleLimits.NameMax + 1) }, None));
        (await store.GetAsync(styles[0].Id, None)).ShouldNotBeNull().Motion.ShouldBe(MotionIntensity.Calm);
    }

    [Fact]
    public async Task TheChannelPhotoIsStoredWithTheProfile()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profiles = new ProfileStore(h.Database);
        var profile = new Profile { Name = "Channel", ChannelPhoto = "Channel/channel/abc-photo.png" };
        await profiles.SaveAsync(profile, None);
        (await profiles.GetAsync(profile.Id, None)).ShouldNotBeNull().ChannelPhoto.ShouldBe("Channel/channel/abc-photo.png");

        profile.ChannelPhoto = null;
        await profiles.SaveAsync(profile, None);
        (await profiles.GetAsync(profile.Id, None)).ShouldNotBeNull().ChannelPhoto.ShouldBeNull();
    }

    [Fact]
    public async Task TemplatesGoWithTheSettingsAndTheChannelPhotoWithTheProfilesIntoABackupAndComeBack()
    {
        using var source = await StorageHarness.CreatedAsync();
        Directory.CreateDirectory(Path.Combine(source.Paths.LibraryDirectory, "Kept channel", "channel"));
        await File.WriteAllBytesAsync(Path.Combine(source.Paths.LibraryDirectory, "Kept channel", "channel", "me.png"), [9, 8, 7], None);
        var profile = new Profile { Name = "Kept channel", ChannelPhoto = "Kept channel/channel/me.png" };
        await new ProfileStore(source.Database).SaveAsync(profile, None);
        var style = new VideoStyle { Name = "Kept look", PaletteId = "forest", Closing = ClosingPlacement.Screen };
        await new VideoStyleStore(source.Database).SaveAsync(style, None);
        var service = Service(source);
        await service.SetPasswordAsync(Password, None);
        var all = Path.Combine(source.Paths.DataDirectory, "all" + BackupRules.Extension);
        var noSettings = Path.Combine(source.Paths.DataDirectory, "no-settings" + BackupRules.Extension);
        var noProfiles = Path.Combine(source.Paths.DataDirectory, "no-profiles" + BackupRules.Extension);
        await service.CreateAsync(all, BackupContents.Automatic, None);
        await service.CreateAsync(noSettings, new BackupContents { Settings = false }, None);
        await service.CreateAsync(noProfiles, new BackupContents { Profiles = false, Concepts = false }, None);

        using (var target = new StorageHarness())
        {
            var report = await Service(target).RestoreAsync(all, Password, target.Protector, None);
            report.Images.ShouldBe(1, "the channel photo is a file of the backup");
            var back = (await new VideoStyleStore(target.Database).ListAsync(None)).ShouldHaveSingleItem();
            (back.Id, back.Name, back.PaletteId, back.Closing).ShouldBe((style.Id, "Kept look", "forest", ClosingPlacement.Screen));
            (await new ProfileStore(target.Database).GetAsync(profile.Id, None)).ShouldNotBeNull().ChannelPhoto.ShouldBe("Kept channel/channel/me.png");
            (await File.ReadAllBytesAsync(Path.Combine(target.Paths.LibraryDirectory, "Kept channel", "channel", "me.png"), None)).ShouldBe([9, 8, 7]);
        }

        using (var target = new StorageHarness())
        {
            await Service(target).RestoreAsync(noSettings, Password, target.Protector, None);
            (await new VideoStyleStore(target.Database).ListAsync(None)).ShouldBeEmpty("templates go with the settings");
            (await new ProfileStore(target.Database).GetAsync(profile.Id, None)).ShouldNotBeNull();
        }

        using (var target = new StorageHarness())
        {
            var report = await Service(target).RestoreAsync(noProfiles, Password, target.Protector, None);
            report.Images.ShouldBe(0);
            File.Exists(Path.Combine(target.Paths.LibraryDirectory, "Kept channel", "channel", "me.png")).ShouldBeFalse();
            (await new VideoStyleStore(target.Database).ListAsync(None)).ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task AFileOf0118WithDataIsUpgradedToTemplatesAndTheChannelPhoto()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profileId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

        // The file as 0.1.18 left it: its two steps, then its data (the profile as 0.1.18 wrote it).
        var steps = await h.QueryAsync(async db =>
        {
            var all = db.Database.GetMigrations().ToList();
            all.Select(SchemaUpgrader.Name).ShouldBe(["Version0117", "Version0118", "Version0119", "Version0120"]);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("0", None);
            await migrator.MigrateAsync(all[1], None);
            return all;
        });
        await using (var old = Version0118Context.Open(h.ConnectionString))
        {
            old.Profiles.Add(new OldProfileRow
            {
                Id = profileId.ToString("D").ToUpperInvariant(), Name = "Old channel", ContentLanguage = ContentLanguages.Turkish, Format = 1,
                TargetSeconds = 90, ConceptText = "Ottoman history.", TemplateId = "history.ottoman", ConceptCounter = 8, CreatedAtUtc = at.UtcTicks,
                UpdatedAtUtc = at.UtcTicks,
            });
            await old.SaveChangesAsync(None);
            old.Concepts.Add(OldConceptRow.Of(new TMConcept.Core.Concepts.Concept
            {
                ProfileId = profileId, Title = "Mohács", Summary = "s", Content = "c", Language = ContentLanguages.Turkish,
            }, 8));
            await old.SaveChangesAsync(None);
        }

        h.Vault.Lock();
        await h.Vault.OpenAsync(None);

        (await h.QueryAsync(async db => (await db.Database.GetAppliedMigrationsAsync(None)).ToList())).ShouldBe(steps);
        var profile = (await new ProfileStore(h.Database).GetAsync(profileId, None)).ShouldNotBeNull();
        (profile.Name, profile.ContentLanguage, profile.Format, profile.TargetSeconds, profile.TemplateId, profile.ChannelPhoto)
            .ShouldBe(("Old channel", ContentLanguages.Turkish, VideoFormat.Horizontal, 90, "history.ottoman", (string?)null));
        profile.ConceptCounter.ShouldBe(8, "the counter of 0.1.18 (with the concept added before the upgrade) is kept");
        profile.CreatedAtUtc.ShouldBe(at);
        (await new ConceptStore(h.Database).HistoryAsync(profileId, None)).ShouldHaveSingleItem().Title.ShouldBe("Mohács");
        (await new VideoStyleStore(h.Database).ListAsync(None)).ShouldBeEmpty("no template is made by the upgrade");

        profile.ChannelPhoto = "Old channel/channel/photo.png";
        await new ProfileStore(h.Database).SaveAsync(profile, None);
        (await new ProfileStore(h.Database).GetAsync(profileId, None)).ShouldNotBeNull().ChannelPhoto.ShouldBe("Old channel/channel/photo.png");
        await new VideoStyleStore(h.Database).SaveAsync(new VideoStyle { Name = "First" }, None);
        (await new VideoStyleStore(h.Database).ListAsync(None)).ShouldHaveSingleItem().Name.ShouldBe("First");
    }

    private static BackupService Service(StorageHarness h) =>
        new(h.Paths, h.Key, h.Vault, h.Database, KdfParameters.Fast, TimeProvider.System, NullLogger<BackupService>.Instance);
}
