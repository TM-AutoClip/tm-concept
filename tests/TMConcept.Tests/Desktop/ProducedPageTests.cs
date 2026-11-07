using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;
using TMConcept.Core.Concepts;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

public sealed class ProducedPageTests
{
    [AvaloniaFact]
    public async Task DetailsShowTheVoiceNotesAndDeletingCanMoveTheFilesToTheRecycleBin()
    {
        var confirm = new FakeConfirm { Checked = true };
        var bin = new FakeBin();
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConfirmDialog>(confirm);
            s.AddSingleton<IRecycleBin>(bin);
        });
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.Turkish, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var folder = Path.Combine(h.Get<IAppPaths>().LibraryDirectory, "Byzantium", "2026-11-05-1-greek-fire");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "video.mp4"), new byte[16]);
        var video = new VideoRecord
        {
            ProfileId = profile.Id,
            Title = "Greek fire",
            Folder = LibraryPaths.Relative(h.Get<IAppPaths>().LibraryDirectory, folder),
            FileName = "video.mp4",
            Engine = "HyperFrames",
            Warnings = VideoWarnings.AiVoice,
            TotalUsd = 0.12m,
        };
        await h.Get<IVideoStore>().AddAsync(video,
        [
            new VideoCost { VideoId = video.Id, Step = "video-code", Usd = 0.10m, Exact = true },
            new VideoCost { VideoId = video.Id, Step = "image-prompt", Usd = 0.02m, Exact = true },
            new VideoCost { VideoId = video.Id, Step = "concepts", Usd = 0.022m, Exact = true },
        ], CancellationToken.None);
        var page = h.Get<ProducedViewModel>();
        await page.ActivateAsync();

        page.Selected = page.Items.Single();
        for (var i = 0; i < 100 && page.Details.Length == 0; i++)
        {
            await Task.Delay(20);
        }

        page.Details.ShouldContain(h.Get<Localizer>()["produced.note.ai_voice"]);
        page.Items.Single().Badges.ShouldNotBeEmpty();

        // The cost lines follow the work, with readable names, and the total says the concept share is in it.
        var text = h.Get<Localizer>();
        int At(string step) => page.Details.IndexOf(text["produced.cost." + step], StringComparison.Ordinal);
        string[] order = ["concepts", "narration-text", "image-prompt", "video-code"];
        order.Select(At).ShouldAllBe(i => i >= 0);
        order.Select(At).ShouldBeInOrder();
        page.Details.ShouldNotContain("step.");
        page.Details.ShouldContain(text.Format("produced.detail.total_share", text.Usd(0.142m), text.Usd(0.022m), text.Usd(0.12m)));
        foreach (var step in VideoCostBreakdown.Order)
        {
            text["produced.cost." + step].ShouldNotStartWith("produced.cost.");
        }

        // The in-app player takes the chosen video but never starts by itself.
        page.Player.IsPlaying.ShouldBeFalse();
        page.Player.ShowPoster.ShouldBeTrue();
        page.Player.DurationMs.ShouldBe(video.DurationMs);
        await page.DeleteAsync();

        bin.Moved.Single().ShouldBe(folder);
        page.Items.ShouldBeEmpty();
        (await h.Get<IVideoStore>().ListAsync(profile.Id, CancellationToken.None)).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task AProducedConceptCanBeProducedAgainAndAMissingFileIsShown()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "A weapon.", Content = "[on screen: ships]\nFire on water.",
            Language = "en", Status = ConceptStatus.Produced };
        await h.Get<IConceptStore>().AddAsync([concept], CancellationToken.None);
        var video = new VideoRecord
        {
            ConceptId = concept.Id,
            ProfileId = profile.Id,
            Title = "Greek fire",
            Folder = "Byzantium/gone",
            FileName = "video.mp4",
            PromptVersion = "2",
        };
        await h.Get<IVideoStore>().AddAsync(video, [], CancellationToken.None);
        var page = h.Get<ProducedViewModel>();
        await page.ActivateAsync();
        var text = h.Get<Localizer>();

        var row = page.Items.Single();
        row.Badges.ShouldStartWith(text["produced.badge.missing"]);
        page.Selected = row;
        for (var i = 0; i < 100 && page.ConceptText.Length == 0; i++)
        {
            await Task.Delay(20);
        }

        page.ConceptText.ShouldContain("Fire on water.");
        page.ConceptText.ShouldContain("2");
        page.Player.Message.ShouldBe(text["player.file_missing"]);
        page.Play();
        page.Status.ShouldBe(text["produced.file_missing"]);

        await page.ProduceAgainAsync();

        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Approved);
        var videoPage = h.Get<VideoViewModel>();
        await videoPage.ActivateAsync();
        videoPage.Approved.ShouldContain(c => c.Id == concept.Id);
    }

    private sealed class FakeBin : IRecycleBin
    {
        public List<string> Moved { get; } = [];

        public Task MoveAsync(string folder, CancellationToken cancellationToken)
        {
            Moved.Add(folder);
            return Task.CompletedTask;
        }
    }
}
