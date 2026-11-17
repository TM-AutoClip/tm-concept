using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.18 (the developer, 17 Nov 2026): 👍 / 👎 under a made video's preview on the Produced page and in Concept → video, one vote for all
/// versions of the video, pressing the same button again clears it, and the vote reaches the next topic and concept prompts.
/// </summary>
public sealed class VideoRatingTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<(Profile Profile, Concept Concept)> PrepareAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<AppSession>().SelectProfileAsync(profile, None);
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "A secret weapon. It burned on water.", Content = "c",
            Language = "en", Status = ConceptStatus.Produced };
        await h.Get<IConceptStore>().AddAsync([concept], None);
        foreach (var version in new[] { 1, 2 })
        {
            var folder = Path.Combine(h.Get<IAppPaths>().LibraryDirectory, "Byzantium", "2026-11-17-1-greek-fire", VersionFolders.Version(version));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "video.mp4"), new byte[16]);
            await h.Get<IVideoStore>().AddAsync(new VideoRecord
            {
                ProfileId = profile.Id, ConceptId = concept.Id, Title = "Greek fire", FileName = "video.mp4", DurationMs = 10_000,
                Folder = LibraryPaths.Relative(h.Get<IAppPaths>().LibraryDirectory, folder), CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(version),
            }, [], None);
        }

        return (profile, concept);
    }

    [AvaloniaFact]
    public async Task OneVoteForAllVersionsThatPressingAgainClearsAndThatSteersThePrompts()
    {
        using var h = new DesktopHarness();
        var (profile, concept) = await PrepareAsync(h);
        var page = h.Get<ProducedViewModel>();
        await page.ActivateAsync();
        page.Selected = page.Items.Single();
        page.Versions.Count.ShouldBe(2);
        var rating = page.Selected.Rating.ShouldNotBeNull();
        page.Versions.ShouldAllBe(v => ReferenceEquals(v.Rating, rating), "the versions of one video share one vote");
        rating.Vote.ShouldBeNull("producing a video does not rate it");
        (await h.Get<IPromptMemorySource>().ReadAsync(profile, None)).LikedVideos.ShouldBeNull("producing is no like");

        await rating.LikeCommand.ExecuteAsync(null);
        rating.IsLiked.ShouldBeTrue();
        (await h.Get<IVideoRatingStore>().ListAsync(profile.Id, None)).ShouldHaveSingleItem().Vote.ShouldBe(VideoVote.Like);
        (await h.Get<IPromptMemorySource>().ReadAsync(profile, None)).LikedVideos.ShouldBe("\n- Greek fire: A secret weapon. It burned on water.");

        await rating.LikeCommand.ExecuteAsync(null);
        rating.Vote.ShouldBeNull("the same button again clears the vote");
        (await h.Get<IVideoRatingStore>().ListAsync(profile.Id, None)).ShouldBeEmpty();

        await rating.DislikeCommand.ExecuteAsync(null);
        rating.IsDisliked.ShouldBeTrue();
        var memory = await h.Get<IPromptMemorySource>().ReadAsync(profile, None);
        memory.DislikedVideos.ShouldBe("\n- Greek fire: A secret weapon. It burned on water.");
        memory.LikedVideos.ShouldBeNull();

        // Concept → video shows the same vote under its preview.
        var video = h.Get<VideoViewModel>();
        await video.ActivateAsync();
        video.SelectedProduced = video.Produced.Single();
        video.SelectedProduced.Rating.ShouldNotBeNull().IsDisliked.ShouldBeTrue();

        // The concept is deleted: the vote stays, with its copies.
        await h.Get<IConceptStore>().TrashAsync([concept.Id], TrashReason.None, None);
        await h.Get<IConceptStore>().DeletePermanentlyAsync([concept.Id], None);
        (await h.Get<IPromptMemorySource>().ReadAsync(profile, None)).DislikedVideos.ShouldBe("\n- Greek fire: A secret weapon. It burned on water.");
    }

    [AvaloniaFact]
    public async Task TheButtonsAndTheNoteStandUnderThePreviewInBothLanguages()
    {
        using var h = new DesktopHarness();
        await PrepareAsync(h);
        var page = h.Get<ProducedViewModel>();
        await page.ActivateAsync();
        page.Selected = page.Items.Single();
        var view = new TMConcept.Desktop.Views.ProducedView { DataContext = page };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        h.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var rating = view.FindControl<TMConcept.Desktop.Views.VideoRatingView>("RatingView").ShouldNotBeNull();
        rating.IsEffectivelyVisible.ShouldBeTrue();
        var like = rating.FindControl<Avalonia.Controls.Primitives.ToggleButton>("LikeButton").ShouldNotBeNull();
        like.Content.ShouldBe(h.Get<Localizer>()["video.rating.like"]);
        like.Command!.Execute(null);
        for (var i = 0; i < 100 && page.Selected!.Rating!.Vote is null; i++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }

        like.IsChecked.ShouldBe(true);
        window.Close();

        var text = h.Get<Localizer>();
        text.Use("tr");
        text["video.rating.like"].ShouldContain("Beğendim");
        text["video.rating.dislike"].ShouldContain("Beğenmedim");
        text["video.rating.note"].ShouldBe("Daha iyi öneriler için oylamanı rica ederiz; oyun sonraki önerileri etkiler.");
    }
}
